using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace ZedZone.QuickContinue
{
    /// <summary>
    /// 快速继续游戏（Quick Continue）插件入口：
    /// 1. 在主菜单“联机游戏”按钮正上方新增单人模式专属的“继续游戏 / Continue”按钮；
    /// 2. 自动记录并跟踪最近游玩的单人地图、角色与最新存档槽（主动忽略多人联机会话）；
    /// 3. 首次启用时自动扫描磁盘上最新的单人存档，点击一次即可跳过多层菜单直接进入世界。
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BasePlugin
    {
        public const string PluginGuid = "zedzone.quickcontinue";
        public const string PluginName = "Quick Continue";
        public const string PluginVersion = "1.0.0";

        internal static Plugin Instance;
        internal static ManualLogSource Logs;

        // 通用设置
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> VerboseLog;
        internal static ConfigEntry<float> VerticalSpacing;

        // 最近游玩的单人存档记录
        internal static ConfigEntry<string> LastGameId;
        internal static ConfigEntry<string> LastSaveFilePath;
        internal static ConfigEntry<int> LastSlotIndex;
        internal static ConfigEntry<string> LastCharacterId;
        internal static ConfigEntry<bool> LastIsStoryMode;
        internal static ConfigEntry<long> LastSaveWriteUtcTicks;

        private static readonly HashSet<string> LoggedErrorTags = new HashSet<string>(StringComparer.Ordinal);

        public override void Load()
        {
            Instance = this;
            Logs = Log;

            Enabled = Config.Bind("General", "Enabled", true,
                "启用快速继续游戏（Quick Continue）模组。");
            VerboseLog = Config.Bind("General", "VerboseLog", false,
                "输出详细调试日志到 BepInEx\\LogOutput.log。");
            VerticalSpacing = Config.Bind("General", "VerticalSpacing", 26f,
                new ConfigDescription("主菜单“继续游戏”按钮位于“联机游戏”按钮上方的垂直间距（像素）。",
                    new AcceptableValueRange<float>(0f, 120f)));

            LastGameId = Config.Bind("LastSinglePlayer", "GameId", "",
                "最近一次游玩的单人世界存档 ID。");
            LastSaveFilePath = Config.Bind("LastSinglePlayer", "SaveFilePath", "",
                "最近一次进入或保存的单人 .game 存档文件路径。");
            LastSlotIndex = Config.Bind("LastSinglePlayer", "SlotIndex", -1,
                "最近一次进入或保存的单人存档槽位索引。");
            LastCharacterId = Config.Bind("LastSinglePlayer", "CharacterId", "",
                "最近一次游玩该单人世界时使用的角色档案 ID（剧情模式留空）。");
            LastIsStoryMode = Config.Bind("LastSinglePlayer", "IsStoryMode", false,
                "最近一次游玩的单人存档是否为剧情模式。");
            LastSaveWriteUtcTicks = Config.Bind("LastSinglePlayer", "SaveWriteUtcTicks", 0L,
                "记录该单人存档时的文件 UTC 修改时间戳，用于识别局内自动保存的新槽位。");

            if (!Enabled.Value)
            {
                Log.LogInfo($"{PluginName} is disabled in config.");
                return;
            }

            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<QuickContinueMenuDriver>())
            {
                ClassInjector.RegisterTypeInIl2Cpp<QuickContinueMenuDriver>();
            }

            var harmony = new Harmony(PluginGuid);
            int applied = 0;
            int skipped = 0;

            Type[] patchTypes =
            {
                typeof(Patch_MainMenuController_Start),
                typeof(Patch_MultiplayerPanel_TryInjectMenuButton),
                typeof(Patch_WorldEntry_Enter),
                typeof(Patch_GameDataManager_SaveGameData),
            };

            foreach (var patchType in patchTypes)
            {
                if (PatchOne(harmony, patchType))
                    applied++;
                else
                    skipped++;
            }

            int hooked = harmony.GetPatchedMethods().Count();
            Log.LogInfo($"{PluginName} {PluginVersion}: {applied} patch group(s) applied, {skipped} skipped, {hooked} method(s) hooked.");
        }

        private static bool PatchOne(Harmony harmony, Type patchType)
        {
            try
            {
                harmony.PatchAll(patchType);
                return true;
            }
            catch (Exception ex)
            {
                Logs?.LogWarning($"Patch {patchType.Name} skipped: {ex.Message}");
                return false;
            }
        }

        internal static void LogVerbose(string message)
        {
            if (VerboseLog != null && VerboseLog.Value)
                Logs?.LogInfo("[Verbose] " + message);
        }

        internal static void LogOnce(string tag, Exception ex)
        {
            if (!LoggedErrorTags.Add(tag)) return;
            Logs?.LogWarning($"[{tag}] {ex.GetType().Name}: {ex.Message}");
        }
    }
}
