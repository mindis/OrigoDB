using System;
using System.Threading;
using LiveDomain.Core.Logging;

namespace LiveDomain.Core
{
    /// <summary>
    /// An optimistic kernel writes to the log before aquiring the
    /// write lock and applying the command to the model. If the
    /// command fails a rollback marker is written to the log and
    /// the system is rolled back by doing a full restore.
    /// </summary>
    public sealed class OptimisticKernel : Kernel
    {
        private static ILog _log = LogProvider.Factory.GetLogForCallingType();

        private object commandLock = new object();

        public OptimisticKernel(EngineConfiguration config, IStore store)
            : base(config, store)
        {

        }

        public override object ExecuteCommand(Command command)
        {
            lock (commandLock)
            {
                try
                {
                    _commandJournal.Append(command);
                    _synchronizer.EnterUpgrade();
                    command.PrepareStub(_model);
                    _synchronizer.EnterWrite();
                    try
                    {
                        return command.ExecuteStub(_model);
                    }
                    catch (Exception ex)
                    {
                        _log.Exception(ex);
                        _commandJournal.WriteRollbackMarker();
                        throw;
                    }
                }
                catch (CommandAbortedException ex)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Restore();
                    throw;
                }
                finally
                {
                    _synchronizer.Exit();
                }
            }
        }
    }
}