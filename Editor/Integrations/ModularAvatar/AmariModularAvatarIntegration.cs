using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor.integrations.modular_avatar
{
    public static class AmariModularAvatarIntegration
    {
        public static bool IsInstalled()
        {
#if AMARI_MA_INSTALLED
            return true;
#else
            return false;
#endif
        }

        public static int GetSeverityCount(AmariSeverity severity)
        {
            // TODO 実装
            throw new System.NotImplementedException();
        }
    }
}
