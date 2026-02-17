namespace RJCP.IO
{
    using System;

    /// <summary>
    /// Class AsyncResult based on <see cref="AsyncResult"/> to also provide a result of the asynchronous operation.
    /// </summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    public abstract class AsyncResult<TResult> : AsyncResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncResult{TResult}"/> class.
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
        /// The owner of the <see cref="AsyncResult{TResult}"/> object, normally <c>this</c>. This is used to guard
        /// against the user providing the <see cref="IAsyncResult"/> object to the <c>EndXXX</c> method of a different
        /// object.
        /// </param>
        /// <param name="operationId">
        /// The operation identifier to distinguish this AsyncResult with others from this class. This is used to guard
        /// against the user providing the IAsyncResult to a non-matching <c>EndYYY</c> method (e.g. BeginRead -&gt;
        /// EndWrite)
        /// </param>
        /// <remarks>
        /// You should have implemented your own <c>BeginXXX</c> method of the form:
        /// <para>
        /// <c>public BeginXXX(param1, param2, param3, ..., AsyncCallback asyncCallback, Object state) { ... }</c>
        /// </para>
        /// <para>passing the parameters asyncCallback and state to this constructor.</para>
        /// <para>
        /// The <paramref name="owner"/> is used to detect giving the IAsyncResult object back to the wrong object
        /// instance.
        /// </para>
        /// </remarks>
        protected AsyncResult(AsyncCallback asyncCallback, object state, object owner, string operationId)
            : base(asyncCallback, state, owner, operationId)
        { }

        // Field set when operation completes
        private TResult m_result = default;

        /// <summary>
        /// Sets the result on completion of the background task.
        /// </summary>
        /// <param name="result">The result.</param>
        /// <remarks>
        /// Your derived class should call this method, setting the result of the operation before it calls the
        /// AsyncResult.Complete method.
        /// </remarks>
        protected void SetResult(TResult result)
        {
            m_result = result;
        }

        /// <summary>
        /// Called by your own <c>EndXXX</c> method, to clean up resources and wait for a result if not already
        /// finished.
        /// </summary>
        /// <param name="result">The IAsyncResult object, your own object derived from <see cref="AsyncResult"/>.</param>
        /// <param name="owner">
        /// The object calling this method. This should be the same owner as when the object was instantiated.
        /// </param>
        /// <param name="operationId">
        /// The operation identifier. This should be the same string identifier as when the object was instantiated
        /// </param>
        /// <returns>The result of the operation, previously set by the method <seealso cref="SetResult"/>.</returns>
        /// <exception cref="ArgumentException">
        /// Result passed represents an operation not supported by this framework. Occurs as an IAsyncResult object not
        /// derived from this class was provided.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// End was called on a different object than begin.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// End was called multiple times for this operation.
        /// </exception>
        /// <exception cref="ArgumentException">End operation type was different than Begin.</exception>
        new public static TResult End(IAsyncResult result, object owner, string operationId)
        {
            AsyncResult<TResult> asyncResult = result as AsyncResult<TResult>;

            // Wait until operation has completed
            AsyncResult.End(result, owner, operationId);

            // Return the result (if above didn't throw)
            return asyncResult.m_result;
        }
    }
}
