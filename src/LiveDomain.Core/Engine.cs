﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Diagnostics;
using System.Reflection;

namespace LiveDomain.Core
{


    /// <summary>
    /// Engine is responsible for executing commands and queries against
    /// the model while conforming to ACID.
    /// </summary>
    /// <typeparam name="M"></typeparam>
	public class Engine : IDisposable
    {

        /// <summary>
        /// The prevalent system. The database. Your single aggregate root. The graph.
        /// </summary>
        protected Model _theModel;

        /// <summary>
        /// The time to wait for the lock before a TimeoutException is thrown.
        /// </summary>
        public TimeSpan LockTimeout { get; set; }

        /// <summary>
        /// The command journal implementation
        /// </summary>
        internal ICommandJournal CommandJournal { get; private set; }


        /// <summary>
        /// Make a deep copy of all command and query results so no references are passed out from the model.
        /// <remarks>
        /// Set to false if you are certain that results will not be modified by client code. Note also that 
        /// the state of the resultset can be modified by a subsequent command rendering the result graph inconsistent.</remarks>
        /// </summary>
        /// 
        public bool CloneResults { get; set; }



        Storage _storage;
        ILockStrategy _lock;
        Serializer _serializer;
        bool _isDisposed = false;
        EngineSettings _settings;


        /// <summary>
        /// Shuts down the engine
        /// </summary>
        public void Close()
        {
            //TODO: Do we need a close? How about an Offline/Online
            if (!_isDisposed)
            {
                try
                {
                    _lock.EnterWrite();
                    CommandJournal.Close();
                    _isDisposed = true;

                }
                finally
                {
                    _lock.Exit();
                }
            }
        }


        private void Restore()
        {
            CommandJournal.Close();

            _theModel = _storage.ReadModel();

            foreach (var command in CommandJournal.OfType<JournalEntry<Command>>().Select(entry => entry.Item))
            {
                command.Redo(_theModel);
            }
        }

        /// <summary>
        /// Writes the model to disk and clears the journal
        /// </summary>
        public void WriteBaseImage()
        {
            ThrowIfDisposed();
            try
            {
                _lock.EnterRead();
                _storage.WriteModel(_theModel);
                CommandJournal.Clear();
            }
            finally
            {
                _lock.Exit();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed) throw new InvalidOperationException("Engine has been disposed");
        }

        private void CallPrepare(Command command)
        {
            try
            {
                command.PrepareStub(_theModel);
            }
            catch (Exception ex)
            {

                throw new CommandFailedException("Exception thrown during Prepare(), see inner exception for details", ex);
            }
        }

        protected Engine(EngineSettings settings)
        {
            _settings = settings;
            _storage = new Storage(_settings);
            _lock = _settings.CreateLockingStrategy();
            _serializer = new Serializer(_settings.CreateSerializationFormatter());
            CommandJournal = _storage.CreateCommandJournal();
        }


        #region Execute overloads
        
        public object Execute(Query query)
        {
        	return Execute<Model, object>(M => query.ExecuteStub(M));
        }

		public T Execute<M, T>(Func<M, T> query) where M : Model
        {
            ThrowIfDisposed();
            _lock.EnterRead();

            try
            {
                T result = query.Invoke(_theModel as M);
                if (CloneResults) result = _serializer.Clone(result);
                return result;
            }
            finally
            {
                _lock.Exit();
            }

        }

        public object Execute(Command command)
        {
            ThrowIfDisposed();

            _lock.EnterUpgrade();
            try
            {
                CallPrepare(command);
                _lock.EnterWrite();
                object result = command.ExecuteStub(_theModel);
                CommandJournal.Append(command);
                if (CloneResults) result = _serializer.Clone(result);
                return result;
            }
            catch (TimeoutException) { throw; }
            catch (CommandFailedException) { throw; }
            catch (FatalException) { Close(); throw; }
            catch (Exception ex) 
            {
                Restore();//TODO: Or shutdown based on setting
                throw new CommandFailedException("Command threw an exception, state was rolled back, see inner exception for details", ex);
            }
            finally
            {
                _lock.Exit();
            }
        }

        #endregion


       


        #region Snapshot methods


        public IEnumerable<SnapshotInfo> GetSnapshots() 
        { 
            return _storage.GetSnapshots(); 
        }

        /// <summary>
        /// Discards the commands in the journal and restores from latest image
        /// </summary>
        public void RevertToImage()
        {
            Revert(String.Empty);
        }

        /// <summary>
        /// Shared by RevertToSnapshot and RevertToImage. Behaviour is identical, source file differs.
        /// </summary>
        /// <param name="name"></param>
        private void Revert(string name)
        {

            bool isSnapshot = !String.IsNullOrEmpty(name);
            try
            {
                _lock.EnterWrite();
                _theModel = isSnapshot ? _storage.ReadSnapshot(name) : _storage.ReadModel();
                CommandJournal.Clear();
            }
            finally
            {
                _lock.Exit();
            }
        }

        public void RevertToSnapshot(string name)
        {
            Revert(name);
        }

        public void RevertToSnapshot(SnapshotInfo snapshotInfo)
        {
            RevertToSnapshot(snapshotInfo.Name);
        }

        public void CreateSnapshot(string pathRelativeToSnapshotDir)
        {
            try
            {
                _lock.EnterRead();
                _storage.WriteSnapshot(pathRelativeToSnapshotDir, _theModel);
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                _lock.Exit();
            }
        }

        #endregion

        public void Dispose()
        {
            this.Close();
        }


        #region Static Load and Create methods

        public static Engine Load(EngineSettings settings)
        {
            var engine = new Engine(settings);
			engine.Restore();
            return engine;
        }

        public static void Create(Model model, EngineSettings settings)
        {
            var storage = Storage.Create(settings);
            storage.WriteModel(model);
        }
        
        public static Engine Load(string path)
        {
            return Load(new EngineSettings(path));
        }

    	public static Engine<M> Load<M>(string path) where M : Model
    	{
    		return Load<M>(new EngineSettings(path));
    	}

    	public static Engine<M> Load<M>(EngineSettings settings) where M : Model
    	{
			var engine = new Engine<M>(settings);
			engine.Restore();
    		return engine;
    	}



        /// <summary>
        /// Loads an existing or creates a new Engine using defaults
        /// </summary>
        /// <typeparam name="M"></typeparam>
        /// <returns></returns>

        public static Engine<M> LoadOrCreate<M>(Func<M> constructor = null, string targetDirectory = null, string name = null) where M : Model, new()
        {
            if (constructor == null) constructor = () => new M();

            if (String.IsNullOrEmpty(targetDirectory)) { }
            if (String.IsNullOrEmpty(name)) { }


            try
            {
                bool canLoad = true; //todo
                if (!canLoad) Create(constructor.Invoke(), new EngineSettings(targetDirectory));
                return Load<M>(targetDirectory);

            }
            catch (Exception) //TODO: Add specific exception
            {
                throw;
            }
        }

        #endregion
    }

	public class Engine<M> : Engine, ILocalTransactionHandler<M> where M : Model
    {

        public Engine(EngineSettings settings) : base(settings){}

        //public static Engine<M> Load<M>() where M : Model
        //{
        //    throw new NotImplementedException();
        //}


        public T Execute<T>(Func<M, T> query)
        {
            return base.Execute(query);
        }

    	public T Execute<T>(CommandWithResult<M, T> command)
        {
            return (T) base.Execute(command);
        }

		public void Execute(Command<M> command)
		{
			base.Execute(command);
		}

		public T Execute<T>(Query<M, T> query)
		{
			return (T)base.Execute(query);
		}


		public T Execute<T>(Func<M, T> query, bool cloneResults)
		{
			return base.Execute(query);
		}
    }
}
