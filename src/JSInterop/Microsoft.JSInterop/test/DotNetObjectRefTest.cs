// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Xunit;
using static Microsoft.JSInterop.TestJSRuntime;

namespace Microsoft.JSInterop
{
    public class DotNetObjectRefTest
    {
        [Fact]
        public Task CanAccessValue() => WithJSRuntime(_ =>
        {
            var obj = new object();
            Assert.Same(obj, DotNetObjectRef.Create(obj).Value);
        });

        [Fact]
        public Task NotifiesAssociatedJsRuntimeOfDisposal() => WithJSRuntime(jsRuntime =>
        {
            // Arrange
            var objRef = DotNetObjectRef.Create(new object());

            // Act
            objRef.Dispose();

            // Assert
            var ex = Assert.Throws<ArgumentException>(() => jsRuntime.ObjectRefManager.FindDotNetObject(objRef.ObjectId));
            Assert.StartsWith("There is no tracked object with id '1'.", ex.Message);
        });
    }
}
