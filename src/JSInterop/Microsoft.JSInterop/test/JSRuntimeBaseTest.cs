// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.JSInterop.Tests
{
    public class JSRuntimeBaseTest
    {
        [Fact]
        public void DispatchesAsyncCallsWithDistinctAsyncHandles()
        {
            // Arrange
            var runtime = new TestJSRuntime();

            // Act
            runtime.InvokeAsync<object>("test identifier 1", "arg1", 123, true);
            runtime.InvokeAsync<object>("test identifier 2", "some other arg");

            // Assert
            Assert.Collection(runtime.BeginInvokeCalls,
                call =>
                {
                    Assert.Equal("test identifier 1", call.Identifier);
                    Assert.Equal("[\"arg1\",123,true]", call.ArgsJson);
                },
                call =>
                {
                    Assert.Equal("test identifier 2", call.Identifier);
                    Assert.Equal("[\"some other arg\"]", call.ArgsJson);
                    Assert.NotEqual(runtime.BeginInvokeCalls[0].AsyncHandle, call.AsyncHandle);
                });
        }

        [Fact]
        public async Task InvokeAsync_CancelsAsyncTask_AfterDefaultTimeout()
        {
            // Arrange
            var runtime = new TestJSRuntime();
            runtime.DefaultTimeout = TimeSpan.FromSeconds(1);

            // Act
            var task = runtime.InvokeAsync<object>("test identifier 1", "arg1", 123, true);

            // Assert
            await Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
        }

        [Fact]
        public async Task InvokeAsync_CompletesSuccessfullyBeforeTimeout()
        {
            // Arrange
            var runtime = new TestJSRuntime();
            runtime.DefaultTimeout = TimeSpan.FromSeconds(10);

            // Act
            var task = runtime.InvokeAsync<object>("test identifier 1", "arg1", 123, true);
            runtime.EndInvokeJS(2, succeeded: true, null);

            // Assert
            await task;
        }

        [Fact]
        public async Task InvokeAsync_CancelsAsyncTasksWhenCancellationTokenFires()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var runtime = new TestJSRuntime();

            // Act
            var task = runtime.InvokeAsync<object>("test identifier 1", new object[] { "arg1", 123, true }, cts.Token);

            cts.Cancel();

            // Assert
            await Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
        }

        [Fact]
        public async Task InvokeAsync_DoesNotStartWorkWhenCancellationHasBeenRequested()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var runtime = new TestJSRuntime();

            // Act
            var task = runtime.InvokeAsync<object>("test identifier 1", new object[] { "arg1", 123, true }, cts.Token);

            cts.Cancel();

            // Assert
            await Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
            Assert.Empty(runtime.BeginInvokeCalls);
        }

        [Fact]
        public void CanCompleteAsyncCallsAsSuccess()
        {
            // Arrange
            var runtime = new TestJSRuntime();

            // Act/Assert: Tasks not initially completed
            var unrelatedTask = runtime.InvokeAsync<string>("unrelated call", Array.Empty<object>());
            var task = runtime.InvokeAsync<string>("test identifier", Array.Empty<object>());
            Assert.False(unrelatedTask.IsCompleted);
            Assert.False(task.IsCompleted);
            var bytes = Encoding.UTF8.GetBytes("\"my result\"");


            // Act/Assert: Task can be completed
            runtime.OnEndInvoke(
                runtime.BeginInvokeCalls[1].AsyncHandle,
                /* succeeded: */ true,
                new JSAsyncCallResult(bytes, default));
            Assert.False(unrelatedTask.IsCompleted);
            Assert.True(task.IsCompleted);
            Assert.Equal("my result", task.Result);
        }

        [Fact]
        public void CanCompleteAsyncCallsAsFailure()
        {
            // Arrange
            var runtime = new TestJSRuntime();

            // Act/Assert: Tasks not initially completed
            var unrelatedTask = runtime.InvokeAsync<string>("unrelated call", Array.Empty<object>());
            var task = runtime.InvokeAsync<string>("test identifier", Array.Empty<object>());
            Assert.False(unrelatedTask.IsCompleted);
            Assert.False(task.IsCompleted);
            var bytes = Encoding.UTF8.GetBytes("\"This is a test exception\"");

            // Act/Assert: Task can be failed
            runtime.OnEndInvoke(
                runtime.BeginInvokeCalls[1].AsyncHandle,
                /* succeeded: */ false,
                new JSAsyncCallResult(bytes, default));
            Assert.False(unrelatedTask.IsCompleted);
            Assert.True(task.IsCompleted);

            Assert.IsType<AggregateException>(task.Exception);
            Assert.IsType<JSException>(task.Exception.InnerException);
            Assert.Equal("This is a test exception", ((JSException)task.Exception.InnerException).Message);
        }

        [Fact]
        public async Task CanCompleteAsyncCallsWithErrorsDuringDeserialization()
        {
            // Arrange
            var runtime = new TestJSRuntime();

            // Act/Assert: Tasks not initially completed
            var unrelatedTask = runtime.InvokeAsync<string>("unrelated call", Array.Empty<object>());
            var task = runtime.InvokeAsync<int>("test identifier", Array.Empty<object>());
            Assert.False(unrelatedTask.IsCompleted);
            Assert.False(task.IsCompleted);
            var bytes = Encoding.UTF8.GetBytes("Not a string");

            // Act/Assert: Task can be failed
            runtime.OnEndInvoke(
                runtime.BeginInvokeCalls[1].AsyncHandle,
                /* succeeded: */ true,
                new JSAsyncCallResult(bytes, state: default));
            Assert.False(unrelatedTask.IsCompleted);

            var jsException = await Assert.ThrowsAsync<JSException>(() => task);
            Assert.IsAssignableFrom<JsonException>(jsException.InnerException);
        }

        [Fact]
        public async Task CompletingSameAsyncCallMoreThanOnce_IgnoresSecondResultAsync()
        {
            // Arrange
            var runtime = new TestJSRuntime();

            // Act/Assert
            var task = runtime.InvokeAsync<string>("test identifier", Array.Empty<object>());
            var asyncHandle = runtime.BeginInvokeCalls[0].AsyncHandle;
            var firstBytes = Encoding.UTF8.GetBytes("\"Some data\"");
            var secondBytes = Encoding.UTF8.GetBytes("\"Exception\"");

            runtime.OnEndInvoke(asyncHandle, true, new JSAsyncCallResult(firstBytes, state: default));
            runtime.OnEndInvoke(asyncHandle, false, new JSAsyncCallResult(secondBytes, state: default));

            var result = await task;
            Assert.Equal("Some data", result);
        }

        [Fact]
        public void SerializesDotNetObjectWrappersInKnownFormat()
        {
            // Arrange
            var runtime = new TestJSRuntime();
            JSRuntime.SetCurrentJSRuntime(runtime);
            var obj1 = new object();
            var obj2 = new object();
            var obj3 = new object();

            // Act
            // Showing we can pass the DotNetObject either as top-level args or nested
            var obj1Ref = DotNetObjectRef.Create(obj1);
            var obj1DifferentRef = DotNetObjectRef.Create(obj1);
            runtime.InvokeAsync<object>("test identifier",
                obj1Ref,
                new Dictionary<string, object>
                {
                    { "obj2", DotNetObjectRef.Create(obj2) },
                    { "obj3", DotNetObjectRef.Create(obj3) },
                    { "obj1SameRef", obj1Ref },
                    { "obj1DifferentRef", obj1DifferentRef },
                });

            // Assert: Serialized as expected
            var call = runtime.BeginInvokeCalls.Single();
            Assert.Equal("test identifier", call.Identifier);
            Assert.Equal("[{\"__dotNetObject\":1},{\"obj2\":{\"__dotNetObject\":3},\"obj3\":{\"__dotNetObject\":4},\"obj1SameRef\":{\"__dotNetObject\":1},\"obj1DifferentRef\":{\"__dotNetObject\":2}}]", call.ArgsJson);

            // Assert: Objects were tracked
            Assert.Same(obj1, runtime.ObjectRefManager.FindDotNetObject(1));
            Assert.Same(obj1, runtime.ObjectRefManager.FindDotNetObject(2));
            Assert.Same(obj2, runtime.ObjectRefManager.FindDotNetObject(3));
            Assert.Same(obj3, runtime.ObjectRefManager.FindDotNetObject(4));
        }

        [Fact]
        public void CanSanitizeDotNetInteropExceptions()
        {
            // Arrange
            var expectedMessage = "An error ocurred while invoking '[Assembly]::Method'. Swapping to 'Development' environment will " +
                "display more detailed information about the error that occurred.";

            string GetMessage(string assembly, string method) => $"An error ocurred while invoking '[{assembly}]::{method}'. Swapping to 'Development' environment will " +
                "display more detailed information about the error that occurred.";

            var runtime = new TestJSRuntime()
            {
                OnDotNetException = (e, a, m) => new JSError { Message = GetMessage(a, m) }
            };

            var exception = new Exception("Some really sensitive data in here");

            // Act
            runtime.EndInvokeDotNet("0", false, exception, "Assembly", "Method", 0);

            // Assert
            var call = runtime.EndInvokeDotNetCalls.Single();
            Assert.Equal("0", call.CallId);
            Assert.False(call.Success);
            var jsError = Assert.IsType<JSError>(call.ResultOrError);
            Assert.Equal(expectedMessage, jsError.Message);
        }

        private class JSError
        {
            public string Message { get; set; }
        }

        class TestJSRuntime : JSRuntimeBase
        {
            public List<BeginInvokeAsyncArgs> BeginInvokeCalls = new List<BeginInvokeAsyncArgs>();
            public List<EndInvokeDotNetArgs> EndInvokeDotNetCalls = new List<EndInvokeDotNetArgs>();

            public TimeSpan? DefaultTimeout
            {
                set
                {
                    base.DefaultAsyncTimeout = value;
                }
            }

            public class BeginInvokeAsyncArgs
            {
                public long AsyncHandle { get; set; }
                public string Identifier { get; set; }
                public string ArgsJson { get; set; }
            }

            public class EndInvokeDotNetArgs
            {
                public string CallId { get; set; }
                public bool Success { get; set; }
                public object ResultOrError { get; set; }
            }

            public Func<Exception, string, string, object> OnDotNetException { get; set; }

            protected internal override void EndInvokeDotNet(string callId, bool success, object resultOrError, string assemblyName, string methodIdentifier, long dotNetObjectId)
            {
                if (OnDotNetException != null && !success)
                {
                    resultOrError = OnDotNetException(resultOrError as Exception, assemblyName, methodIdentifier);
                }

                EndInvokeDotNetCalls.Add(new EndInvokeDotNetArgs
                {
                    CallId = callId,
                    Success = success,
                    ResultOrError = resultOrError
                });
            }

            protected override void BeginInvokeJS(long asyncHandle, string identifier, string argsJson)
            {
                BeginInvokeCalls.Add(new BeginInvokeAsyncArgs
                {
                    AsyncHandle = asyncHandle,
                    Identifier = identifier,
                    ArgsJson = argsJson,
                });
            }

            public void OnEndInvoke(long asyncHandle, bool succeeded, JSAsyncCallResult callResult)
                => EndInvokeJS(asyncHandle, succeeded, callResult);
        }
    }
}
