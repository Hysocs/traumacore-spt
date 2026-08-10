using System;
using BepInEx.Configuration;

namespace TraumaCore
{
    // Configuration Manager reads optional metadata by this conventional type
    // name, so the mod remains independent of its assembly at runtime.
    internal sealed class ConfigurationManagerAttributes
    {
        public Action<ConfigEntryBase> CustomDrawer;
        public string Category;
        public string DispName;
        public int Order;
        public bool HideDefaultButton;
        public bool HideSettingName;
    }
}
