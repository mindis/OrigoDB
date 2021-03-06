using System;

namespace OrigoDB.Core
{

    /// <summary>
    /// Shared by Query and CommandWithResult
    /// </summary>
    public interface IOperationWithResult
    {
        /// <summary>
        /// True if results are safe to return to client, default is false.
        /// Set to true if your command implementation gaurantees no references to
        /// mutable objects within the model are returned.
        /// </summary>
        bool ResultIsSafe { get;}
    }
}