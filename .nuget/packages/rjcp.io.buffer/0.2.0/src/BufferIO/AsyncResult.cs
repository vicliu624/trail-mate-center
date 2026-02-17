namespace RJCP.IO
{
    using System;
    using System.Threading;

#if NET45_OR_GREATER || NETSTANDARD
    using System.Runtime.ExceptionServices;
#endif

    /// <summary>
    /// Class for implementing an AsyncResult method.
    /// </summary>
    /// <remarks>
    /// The code here is almost identical to that provided by the MSDN blog at:
    /// http://blogs.msdn.com/b/nikos/archive/2011/03/14/how-to-implement-iasyncresult-in-another-way.aspx
    /// <para>
    /// While the code on the blog is sparse, this class can make it considerably easier for you to implement your
    /// (a)synchronous classes.
    /// </para>
    /// <para>
    /// You would create your own class, derived from this class, which at a minimum implements the
    /// <see cref="Process"/> method. The <see cref="Process"/> method is what starts the asynchronous method, by
    /// talking to hardware, starting another asynchronous method, or creating a thread to start the asynchronous
    /// method.
    /// </para>
    /// <para>
    /// Implement your class that you can set all the properties before calling <see cref="Process"/>, as that method
    /// itself doesn't allow for any parameters.
    /// </para>
    /// <para>
    /// In your main class that provides the <c>BeginXXX</c> and <c>EndXXX</c> methods, you would then instantiate and
    /// use your AsyncResult class.
    /// </para>
    /// <para>For <c>BeginXXX</c> your code would look like:</para>
    /// <code>
    /// <![CDATA[
    /// public IAsyncResult BeginXXX(object par1, object par2, AsyncCallback asyncCallback, object state)
    /// {
    ///     XXXAsyncResult result = new XXXAsyncResult(par1, par2, asyncCallback, state, this, "XXX");
    ///     result.Process();
    ///     return result;
    /// }
    /// ]]>
    /// </code>
    /// <para>
    /// That creates a new IAsyncResult object, based on your own class <c>XXXAsyncResult</c> which derives from
    /// <see cref="AsyncResult"/>. It begins processing and you return the <see cref="IAsyncResult"/> object which may
    /// or may not be finished. The last four parameters in your AsyncResult's constructor are used to instantiate the
    /// AsyncResult base constructor.
    /// </para>
    /// <para>The <c>EndXXX</c> method then looks like</para>
    /// <code>
    /// <![CDATA[
    /// public void EndXXX(IAsyncResult result)
    /// {
    ///     AsyncResult.End(result, this, "XXX");
    /// }
    /// ]]>
    /// </code>
    /// <para>
    /// The <see cref="AsyncResult.End(System.IAsyncResult, object, string)"/> method checks for you that the user
    /// called your <c>EndXXX</c> method in the correct context, else it raises an exception.
    /// </para>
    /// <para>
    /// The implementation of your own AsyncResult class is derived from the <see cref="AsyncResult"/> class.
    /// </para>
    /// <code>
    /// <![CDATA[
    /// internal class XXXAsyncResult : AsyncResult
    /// {
    ///     private object m_Par1;
    ///     private object m_Par2;
    ///
    ///     public XXXAsyncResult(object par1, object par2,
    ///         AsyncCallback asyncCallback, object state,
    ///         object owner, string operationId)
    ///         : base(asyncCallback, state, owner, operationId)
    ///     {
    ///         m_Par1 = par1; m_Par2 = par2;
    ///     }
    ///
    ///     public override void Process()
    ///     {
    ///         Exception exception = null;
    ///         bool synchronous = false;
    ///         try {
    ///             // Do something with m_Par1 and m_Par2. This may be
    ///             // creating a new thread
    ///             ...
    ///
    ///             // Indicates that the work is finished without running
    ///             // in the background.
    ///             synchronous = true;
    ///         } catch (System.Exception e) {
    ///             exception = e;
    ///         }
    ///         Complete(exception, synchronous);
    ///     }
    /// }
    /// ]]>
    /// </code>
    /// </remarks>
    public abstract class AsyncResult : IAsyncResult
    {
        // Fields set at construction which never change while operation is pending
        private readonly AsyncCallback m_AsyncCallback;
        private readonly object m_AsyncState;

        // Fields set at construction which do change after operation completes
        private const int StatePending = 0;
        private const int StateCompletedSynchronously = 1;
        private const int StateCompletedAsynchronously = 2;
        private int m_CompletedState = StatePending;

        // Field that may or may not get set depending on usage
        private ManualResetEvent m_AsyncWaitHandle;

        // Fields set when operation completes
#if NET45_OR_GREATER || NETSTANDARD
        private ExceptionDispatchInfo m_Exception;
#else
        private Exception m_Exception;
#endif

        /// <summary>
        /// The object which started the operation.
        /// </summary>
        private readonly object m_Owner;

        /// <summary>
        /// Used to verify BeginXXX and EndXXX calls match.
        /// </summary>
        private string m_OperationId;

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncResult"/> class.
        /// </summary>
        /// <param name="asyncCallback">
        /// The method to be called when the asynchronous write operation is completed. This is a part of your
        /// <c>BeginXXX</c> method.
        /// </param>
        /// <param name="state">
        /// A user-provided object that distinguishes this particular asynchronous write request from other requests.
        /// This is a part of your <c>BeginXXX</c> method.
        /// </param>
        /// <param name="owner">
        /// The owner of the AsyncResult object, normally <c>this</c>. This is used to guard against the user providing
        /// the <see cref="IAsyncResult"/> object to the <c>EndXXX</c> method of a different object.
        /// </param>
        /// <param name="operationId">
        /// The operation identifier to distinguish this AsyncResult with others from this class. This is used to guard
        /// against the user providing the IAsyncResult to a non-matching <c>EndYYY</c> method (e.g. BeginRead to
        /// EndWrite).
        /// </param>
        /// <remarks>
        /// You should have implemented your own <c>BeginXXX</c> method of the form:
        /// <para>
        /// <code>public BeginXXX(param1, param2, param3, ..., AsyncCallback asyncCallback, Object state) { ... }</code>
        /// </para>
        /// <para>passing the parameters asyncCallback and state to this constructor.</para>
        /// <para>
        /// The <paramref name="owner"/> is used to detect giving the <see cref="IAsyncResult"/> object back to the
        /// wrong object instance.
        /// </para>
        /// </remarks>
        protected AsyncResult(AsyncCallback asyncCallback, object state, object owner, string operationId)
        {
            m_AsyncCallback = asyncCallback;
            m_AsyncState = state;
            m_Owner = owner;
            m_OperationId = string.IsNullOrEmpty(operationId) ? string.Empty : operationId;
        }

        /// <summary>
        /// This is the method called by your class to start the (a)synchronous operation.
        /// </summary>
        /// <remarks>
        /// Starts the (a)synchronous operation. When the operation is finished, it should call the
        /// <see cref="Complete(Exception, bool)"/> method. That method is handled by this class and calls the user
        /// callback and sets the semaphore when the operation is finished.
        /// </remarks>
        public virtual void Process() { }

        /// <summary>
        /// Called by the derived object to indicate the asynchronous operation is complete.
        /// </summary>
        /// <param name="exception">
        /// The exception that occurred during the operation. If no exception occurred, this value should be
        /// <see langword="null"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the operation completed, <see langword="false"/> otherwise. The value of
        /// <see langword="false"/> indicates that the operation had previously completed and no operation was done.
        /// </returns>
        /// <remarks>
        /// This method should be called when the asynchronous operation is finished. If an exception is provided, this
        /// exception will be raised when <see cref="End"/> is called.
        /// </remarks>
        protected bool Complete(Exception exception)
        {
            return Complete(exception, false /*completedSynchronously*/);
        }

        /// <summary>
        /// Called by the derived object to indicate the asynchronous operation is complete.
        /// </summary>
        /// <param name="exception">
        /// The exception that occurred during the operation. If no exception occurred, this value should be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="completedSynchronously">
        /// if set to <see langword="true"/> then specifies the operation completed synchronously. Else the operation
        /// completed asynchronously (on a different thread).
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the operation completed, <see langword="false"/> otherwise. The value of
        /// <see langword="false"/> indicates that the operation had previously completed and no operation was done.
        /// </returns>
        protected bool Complete(Exception exception, bool completedSynchronously)
        {
            bool result = false;

            // The m_CompletedState field MUST be set prior calling the callback
            int prevState = Interlocked.Exchange(ref m_CompletedState,
                completedSynchronously ? StateCompletedSynchronously :
                StateCompletedAsynchronously);
            if (prevState == StatePending) {
                // Passing null for exception means no error occurred. This is the common case
#if NET45_OR_GREATER || NETSTANDARD
                m_Exception = exception == null ? null : ExceptionDispatchInfo.Capture(exception);
#else
                m_Exception = exception;
#endif

                // Do any processing before completion.
                Completing(exception, completedSynchronously);

                // If the event exists, set it
                if (m_AsyncWaitHandle != null) m_AsyncWaitHandle.Set();

                MakeCallback(m_AsyncCallback, this);

                // Do any final processing after completion
                Completed(exception, completedSynchronously);

                result = true;
            }

            return result;
        }

        private void CheckUsage(object owner, string operationId)
        {
            if (!ReferenceEquals(owner, m_Owner)) {
                throw new InvalidOperationException(Resources.AsyncResult_EndWithInvalidObject);
            }

            // Reuse the operation ID to detect multiple calls to end.
            if (m_OperationId is null) {
                throw new InvalidOperationException(Resources.AsyncResult_EndMultipleCalls);
            }

            if (!string.Equals(operationId, m_OperationId, StringComparison.Ordinal)) {
                throw new ArgumentException(Resources.AsyncResult_EndInvalidOperation);
            }

            // Mark that End was already called.
            m_OperationId = null;
        }

        /// <summary>
        /// Indicates if this object has been completed with an exception.
        /// </summary>
        /// <remarks>
        /// This allows you to optimize your code (not for external public code) to check if an exception has occurred,
        /// especially useful if this needs to be checked before calling <see cref="End"/> as part of your <c>EndXXX</c>
        /// method.
        /// </remarks>
        protected bool HasExceptionOccurred { get { return m_Exception != null; } }

        /// <summary>
        /// Called by your own <c>EndXXX</c> method, to clean up resources and wait for a result if not already
        /// finished.
        /// </summary>
        /// <param name="result">The IAsyncResult object, your own object derived from <see cref="AsyncResult"/>.</param>
        /// <param name="owner">
        /// The object calling this method. This should be the same owner as when the object was instantiated.
        /// </param>
        /// <param name="operationId">
        /// The operation identifier. This should be the same string identifier as when the object was instantiated.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Result passed represents an operation not supported by this framework. Occurs as an
        /// <see cref="IAsyncResult"/> object not derived from this class was provided.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// End was called on a different object than begin.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// End was called multiple times for this operation.
        /// </exception>
        /// <exception cref="ArgumentException">End operation type was different than Begin.</exception>
        /// <remarks>
        /// Note that by calling this method, the semaphore is disposed, so that it can't be used again.
        /// </remarks>
        public static void End(IAsyncResult result, object owner, string operationId)
        {
            if (!(result is AsyncResult asyncResult)) {
                throw new ArgumentException(Resources.AsyncResult_UnsupportedResult, nameof(result));
            }

            asyncResult.CheckUsage(owner, operationId);

            // This method assumes that only 1 thread calls EndInvoke for this object
            if (!asyncResult.IsCompleted) {
                // If the operation isn't done, wait for it
                asyncResult.AsyncWaitHandle.WaitOne();
                asyncResult.AsyncWaitHandle.Close();
                asyncResult.m_AsyncWaitHandle = null;  // Allow early GC
            }

            // Operation is done: if an exception occurred, throw it
            if (asyncResult.m_Exception != null) {
#if NET45_OR_GREATER || NETSTANDARD
                asyncResult.m_Exception.Throw();
#else
                throw asyncResult.m_Exception;
#endif
            }
        }

        #region Implementation of IAsyncResult
        /// <summary>
        /// Gets a user-defined object that qualifies or contains information about an asynchronous operation.
        /// </summary>
        /// <value>The state of the asynchronous.</value>
        /// <returns>
        /// A user-defined object that qualifies or contains information about an asynchronous operation.
        /// </returns>
        public object AsyncState { get { return m_AsyncState; } }

        /// <summary>
        /// Gets a value that indicates whether the asynchronous operation completed synchronously.
        /// </summary>
        /// <value><see langword="true"/> if completed synchronously; otherwise, <see langword="false"/>.</value>
        /// <returns>
        /// <see langword="true"/> if the asynchronous operation completed synchronously; otherwise,
        /// <see langword="false"/>.
        /// </returns>
        public bool CompletedSynchronously
        {
            get
            {
                return Thread.VolatileRead(ref m_CompletedState) == StateCompletedSynchronously;
            }
        }

        /// <summary>
        /// Gets a <see cref="WaitHandle"/> that is used to wait for an asynchronous operation to complete.
        /// </summary>
        /// <value>The asynchronous wait handle.</value>
        /// <returns>A <see cref="WaitHandle"/> that is used to wait for an asynchronous operation to complete.</returns>
        /// <remarks>
        /// You should not use this property to get a <see cref="WaitHandle"/> after the <see cref="End"/> method is
        /// called. It will result in lost resources, as that wait handle won't be disposed. This appears conform to the
        /// notes in MSDN for <see cref="IAsyncResult.AsyncWaitHandle"/> which states
        /// <para><b>Notes to Implementers</b></para>
        /// <para>
        /// Once created, <see cref="IAsyncResult.AsyncWaitHandle"/> should be kept alive until the user calls the
        /// method that concludes the asynchronous operation. At that time the object behind
        /// <see cref="IAsyncResult.AsyncWaitHandle"/> can be discarded.
        /// </para>
        /// <para>
        /// So it's expected that once the <see cref="End"/> method is called, the
        /// <see cref="IAsyncResult.AsyncWaitHandle"/> won't be used.
        /// </para>
        /// </remarks>
        public WaitHandle AsyncWaitHandle
        {
            get
            {
                if (m_AsyncWaitHandle == null) {
                    bool done = IsCompleted;
                    ManualResetEvent mre = new ManualResetEvent(done);
                    if (Interlocked.CompareExchange(ref m_AsyncWaitHandle,
                        mre, null) != null) {
                        // Another thread created this object's event; dispose the event we just created
                        mre.Close();
                    } else {
                        if (!done && IsCompleted) {
                            // If the operation wasn't done when we created the event but now it is done, set the event
                            m_AsyncWaitHandle.Set();
                        }
                    }
                }
                return m_AsyncWaitHandle;
            }
        }

        /// <summary>
        /// Gets a value that indicates whether the asynchronous operation has completed.
        /// </summary>
        /// <value><see langword="true"/> if this instance is completed; otherwise, <see langword="false"/>.</value>
        /// <returns><see langword="true"/> if the operation is complete; otherwise, <see langword="false"/>.</returns>
        public bool IsCompleted
        {
            get
            {
                return Thread.VolatileRead(ref m_CompletedState) != StatePending;
            }
        }
        #endregion

        #region Extensibility
        /// <summary>
        /// Called by the method <see cref="Complete(Exception, bool)"/> before the user callback is made.
        /// </summary>
        /// <param name="exception">
        /// The exception if one occurred. Set to <see langword="null"/> if no exception occurred.
        /// </param>
        /// <param name="completedSynchronously">
        /// Indicates if the callback completed synchronously or not. If set to <see langword="true"/> the callback
        /// completed synchronously.
        /// </param>
        protected virtual void Completing(Exception exception, bool completedSynchronously) { }

        /// <summary>
        /// Executes the user callback if defined, during the <see cref="Complete(Exception, bool)"/> method call
        /// </summary>
        /// <param name="callback">The user callback.</param>
        /// <param name="result">The <see cref="IAsyncResult"/> object, which is <c>this</c> object.</param>
        protected virtual void MakeCallback(AsyncCallback callback, AsyncResult result)
        {
            callback?.Invoke(result);
        }

        /// <summary>
        /// Called by the method <see cref="Complete(Exception, bool)"/> after the user callback is made.
        /// </summary>
        /// <param name="exception">
        /// The exception if one occurred. Set to <see langword="null"/> if no exception occurred. The exception is from
        /// the asynchronous method call, not the callback.
        /// </param>
        /// <param name="completedSynchronously">
        /// Indicates if the callback completed synchronously or not. If set to <see langword="true"/> the callback
        /// completed synchronously.
        /// </param>
        protected virtual void Completed(Exception exception, bool completedSynchronously) { }
        #endregion
    }
}
