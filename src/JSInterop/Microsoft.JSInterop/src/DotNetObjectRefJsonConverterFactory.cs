// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Microsoft.JSInterop
{
    internal sealed class DotNetObjectReferenceJsonConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            Debug.Assert(
                typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(DotNetObjectRef<>),
                "We expect this to only be called for DotNetObjectRef<T> instances.");
            return true;
        }

        protected override JsonConverter CreateConverter(Type typeToConvert)
        {
            // System.Text.Json handles caching the converters per type on our behalf. No caching is required here.
            var instanceType = typeToConvert.GetGenericArguments()[0];
            var converterType = typeof(DotNetObjectReferenceJsonConverter<>).MakeGenericType(instanceType);

            return (JsonConverter)Activator.CreateInstance(converterType);
        }
    }
}
