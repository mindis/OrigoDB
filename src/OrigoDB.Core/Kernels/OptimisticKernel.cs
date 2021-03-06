using System;

namespace OrigoDB.Core
{
    /// <summary>
    /// An optimistic kernel writes to the log before aquiring the
    /// write lock and applying the command to the model. If the
    /// command fails a rollback marker is written to the log and
    /// the system is rolled back by doing a full restore.
    /// </summary>
    public class OptimisticKernel : Kernel
    {

        public OptimisticKernel(EngineConfiguration config, Model model)
            : base(config, model)
        {

        }

        public override object ExecuteCommand(Command command)
        {
            try
            {
                _synchronizer.EnterUpgrade();
                command.PrepareStub(_model);
                _synchronizer.EnterWrite();
                var result = command.ExecuteStub(_model);
                _model.Revision++;
                return result;
            }
            finally
            {
                _synchronizer.Exit();
            }
        }
    }
}