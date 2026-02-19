using System;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.runtime
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ReadOnlyAttribute : PropertyAttribute
    {
    }
}
