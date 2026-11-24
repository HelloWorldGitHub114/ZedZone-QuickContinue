using System;
using System.Collections.Generic;
using System.IO;
using ZedZone.Net;

namespace ZedZone.QuickContinue
{
    internal sealed class ContinueTarget
    {
        public GameData World { get; }
        public string CharacterId { get; }
        public bool IsStoryMode { get; }
        public string SourcePath { get; }

        public ContinueTarget(GameData world, string characterId, bool isStoryMode, string sourcePath)
        {
            World = world;
            CharacterId = characterId ?? string.Empty;
            IsStoryMode = isStoryMode;
            SourcePath = sourcePath ?? string.Empty;
        }
    }

    /// <summary>
    /// 单人最近存档记录、解析与快速进入服务：
    /// 仅跟踪和恢复单人模式进度，主动跳过联机房主/客户端会话；支持首次启用时从磁盘自动发现最新单人存档。
    /// </summary>
    internal static class ContinueSaveStore
    {
        internal static bool IsMultiplayerActive()
        {
            try
            {
                if (NetCharacterSave.coopSessionActive)
                    return true;

                if (NetLobby.isOpenAsHost || NetLobby.inLobbyAsClient)
                    return true;
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("mp_check", ex);
            }

            return false;
        }

        internal static void RecordSinglePlayerEntry(GameData world, string characterId)
        {
            if (world == null || IsMultiplayerActive()) return;

            try
            {
                string path = world.sourceFilePath ?? string.Empty;
                string charId = world.isStoryMode
                    ? string.Empty
                    : (!string.IsNullOrEmpty(characterId)
                        ? characterId
                        : (world.playerData != null ? (world.playerData.characterId ?? string.Empty) : string.Empty));
                long ticks = GetFileWriteTicksOrNow(path);

                SaveLastSinglePlayerConfig(
                    world.id ?? string.Empty,
                    path,
                    world.saveSlotIndex,
                    charId,
                    world.isStoryMode,
                    ticks);

                Plugin.LogVerbose($"Recorded SP entry: id={world.id}, slot={world.saveSlotIndex}, story={world.isStoryMode}, char={charId}, path={path}");
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("record_entry", ex);
            }
        }

        internal static void RecordSinglePlayerSave(GameData gameData, int slotIndex)
        {
            if (gameData == null || IsMultiplayerActive()) return;

            try
            {
                string worldId = gameData.id ?? string.Empty;
                string path = ResolveWrittenSavePath(gameData, worldId, slotIndex);
                string charId = gameData.isStoryMode ? string.Empty : ResolveActiveSaveCharacterId(gameData);
                long ticks = GetFileWriteTicksOrNow(path);

                SaveLastSinglePlayerConfig(
                    worldId,
                    path,
                    slotIndex,
                    charId,
                    gameData.isStoryMode,
                    ticks);

                Plugin.LogVerbose($"Recorded SP save: id={worldId}, slot={slotIndex}, story={gameData.isStoryMode}, char={charId}, path={path}");
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("record_save", ex);
            }
        }

        internal static bool HasAnySinglePlayerSave()
        {
            try
            {
                var gdm = GameDataManager.instance;
                if (gdm == null) return false;

                var paths = gdm.GetSortedSaveFilePaths(false);
                if (paths == null) return false;

                for (int i = 0; i < paths.Count; i++)
                {
                    string p = paths[i];
                    if (!string.IsNullOrEmpty(p) && File.Exists(p))
                        return true;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("has_saves", ex);
            }

            return false;
        }

        internal static bool TryResolveTarget(out ContinueTarget target)
        {
            target = null;

            try
            {
                var gdm = GameDataManager.instance;
                if (gdm == null) return false;

                var sortedPaths = gdm.GetSortedSaveFilePaths(false);
                var candidates = BuildCandidatePathOrder(sortedPaths);

                GameData world = null;
                string loadedPath = string.Empty;

                for (int i = 0; i < candidates.Count; i++)
                {
                    string path = candidates[i];
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

                    GameData loaded = null;
                    try
                    {
                        loaded = gdm.LoadGameDataFromPath(path);
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogOnce("load_save_path", ex);
                    }

                    if (loaded != null)
                    {
                        world = loaded;
                        loadedPath = path;
                        break;
                    }
                }

                if (world == null) return false;

                string charId = world.isStoryMode ? string.Empty : ResolveCharacterForWorld(world);
                target = new ContinueTarget(world, charId, world.isStoryMode, loadedPath);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("resolve_target", ex);
                return false;
            }
        }

        internal static void TryEnterTarget(ContinueTarget target, Action onAbortedOrFailed)
        {
            if (target == null || target.World == null)
            {
                onAbortedOrFailed?.Invoke();
                return;
            }

            try
            {
                if (target.IsStoryMode)
                {
                    bool ok = WorldEntry.Enter(target.World, null, CharacterSource.Snapshot, false, false);
                    if (!ok)
                        onAbortedOrFailed?.Invoke();
                    return;
                }

                if (string.IsNullOrEmpty(target.CharacterId))
                {
                    GameDataUI.BeginEnterWorld(target.World, null);
                    onAbortedOrFailed?.Invoke();
                    return;
                }

                NetCharacterSave.selectedProfile = target.CharacterId;
                NetCharacterSave.pendingSeedCharacter = null;

                var resolvedTarget = target;
                Il2CppSystem.Action<CharacterSource> onResolved = (Il2CppSystem.Action<CharacterSource>)((CharacterSource source) =>
                {
                    try
                    {
                        bool entered = WorldEntry.Enter(resolvedTarget.World, resolvedTarget.CharacterId, source, false, false);
                        if (!entered)
                            onAbortedOrFailed?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogOnce("enter_resolved", ex);
                        onAbortedOrFailed?.Invoke();
                    }
                });

                Il2CppSystem.Action onCancel = (Il2CppSystem.Action)(() =>
                {
                    onAbortedOrFailed?.Invoke();
                });

                WorldEntry.ResolveSnapshotCharacterSourceInteractive(target.World, target.CharacterId, onResolved, onCancel);
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("try_enter", ex);
                onAbortedOrFailed?.Invoke();
            }
        }

        private static List<string> BuildCandidatePathOrder(Il2CppSystem.Collections.Generic.List<string> sortedPaths)
        {
            var candidates = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddCandidate(string path)
            {
                if (string.IsNullOrEmpty(path)) return;
                if (seen.Add(path))
                    candidates.Add(path);
            }

            string lastGameId = Plugin.LastGameId != null ? (Plugin.LastGameId.Value ?? string.Empty) : string.Empty;
            string lastPath = Plugin.LastSaveFilePath != null ? (Plugin.LastSaveFilePath.Value ?? string.Empty) : string.Empty;
            long recordedTicks = Plugin.LastSaveWriteUtcTicks != null ? Plugin.LastSaveWriteUtcTicks.Value : 0L;

            if (!string.IsNullOrEmpty(lastGameId) && sortedPaths != null)
            {
                string prefix = lastGameId + "_";
                var worldSlots = new List<(string path, long ticks)>();

                for (int i = 0; i < sortedPaths.Count; i++)
                {
                    string p = sortedPaths[i];
                    if (string.IsNullOrEmpty(p) || !File.Exists(p)) continue;

                    string name = Path.GetFileNameWithoutExtension(p) ?? string.Empty;
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(name, lastGameId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    long fileTicks = 0L;
                    try { fileTicks = File.GetLastWriteTimeUtc(p).Ticks; }
                    catch { fileTicks = 0L; }
                    worldSlots.Add((p, fileTicks));
                }

                worldSlots.Sort((a, b) => b.ticks.CompareTo(a.ticks));

                if (worldSlots.Count > 0 && worldSlots[0].ticks > recordedTicks + TimeSpan.TicksPerSecond)
                {
                    AddCandidate(worldSlots[0].path);
                    AddCandidate(lastPath);
                }
                else
                {
                    AddCandidate(lastPath);
                }

                for (int i = 0; i < worldSlots.Count; i++)
                    AddCandidate(worldSlots[i].path);
            }
            else if (!string.IsNullOrEmpty(lastPath))
            {
                AddCandidate(lastPath);
            }

            if (sortedPaths != null)
            {
                for (int i = 0; i < sortedPaths.Count; i++)
                    AddCandidate(sortedPaths[i]);
            }

            return candidates;
        }

        private static string ResolveCharacterForWorld(GameData world)
        {
            string worldId = world.id ?? string.Empty;
            string lastGameId = Plugin.LastGameId != null ? (Plugin.LastGameId.Value ?? string.Empty) : string.Empty;
            string lastCharId = Plugin.LastCharacterId != null ? (Plugin.LastCharacterId.Value ?? string.Empty) : string.Empty;

            if (!string.IsNullOrEmpty(worldId) &&
                string.Equals(worldId, lastGameId, StringComparison.OrdinalIgnoreCase) &&
                IsUsableArchiveCharacter(lastCharId))
            {
                return lastCharId;
            }

            string embeddedCharId = world.playerData != null ? (world.playerData.characterId ?? string.Empty) : string.Empty;
            if (IsUsableArchiveCharacter(embeddedCharId))
                return embeddedCharId;

            string selectedProfile = SafeGetSelectedProfile();
            if (IsUsableArchiveCharacter(selectedProfile))
                return selectedProfile;

            try
            {
                var profiles = NetCharacterSave.ListProfiles();
                if (profiles != null)
                {
                    string bestId = string.Empty;
                    long bestSavedTicks = long.MinValue;

                    for (int i = 0; i < profiles.Count; i++)
                    {
                        var profile = profiles[i];
                        if (profile == null) continue;
                        if (string.IsNullOrEmpty(profile.characterId)) continue;
                        if (!string.IsNullOrEmpty(worldId) &&
                            !string.Equals(profile.lastWorldId, worldId, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        if (!IsUsableArchiveCharacter(profile.characterId)) continue;

                        if (profile.savedAtUtc >= bestSavedTicks)
                        {
                            bestSavedTicks = profile.savedAtUtc;
                            bestId = profile.characterId;
                        }
                    }

                    if (!string.IsNullOrEmpty(bestId))
                        return bestId;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("scan_profiles", ex);
            }

            return null;
        }

        private static bool IsUsableArchiveCharacter(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return false;
            try
            {
                return CharacterArchive.Exists(characterId) && !CharacterArchive.IsDead(characterId);
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveWrittenSavePath(GameData gameData, string worldId, int slotIndex)
        {
            string sourcePath = gameData.sourceFilePath ?? string.Empty;
            string slotSuffix = $"_SlotIndex{slotIndex}.game";

            if (!string.IsNullOrEmpty(sourcePath) &&
                sourcePath.EndsWith(slotSuffix, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(sourcePath))
            {
                return sourcePath;
            }

            try
            {
                var gdm = GameDataManager.instance;
                var paths = gdm != null ? gdm.GetSortedSaveFilePaths(false) : null;
                if (paths != null)
                {
                    for (int i = 0; i < paths.Count; i++)
                    {
                        string p = paths[i];
                        if (string.IsNullOrEmpty(p)) continue;
                        string fileName = Path.GetFileName(p) ?? string.Empty;
                        if (!fileName.EndsWith(slotSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!string.IsNullOrEmpty(worldId) &&
                            fileName.IndexOf(worldId, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                        if (File.Exists(p))
                            return p;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogOnce("resolve_save_path", ex);
            }

            return !string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath) ? sourcePath : string.Empty;
        }

        private static string ResolveActiveSaveCharacterId(GameData gameData)
        {
            string selected = SafeGetSelectedProfile();
            if (!string.IsNullOrEmpty(selected))
                return selected;
            return gameData.playerData != null ? (gameData.playerData.characterId ?? string.Empty) : string.Empty;
        }

        private static string SafeGetSelectedProfile()
        {
            try
            {
                return NetCharacterSave.selectedProfile ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static long GetFileWriteTicksOrNow(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    if (File.Exists(path))
                        return File.GetLastWriteTimeUtc(path).Ticks;
                }
                catch
                {
                    // 忽略磁盘元数据读取异常，回退当前时间
                }
            }
            return DateTime.UtcNow.Ticks;
        }

        private static void SaveLastSinglePlayerConfig(
            string gameId,
            string saveFilePath,
            int slotIndex,
            string characterId,
            bool isStoryMode,
            long ticks)
        {
            if (Plugin.LastGameId != null) Plugin.LastGameId.Value = gameId ?? string.Empty;
            if (Plugin.LastSaveFilePath != null) Plugin.LastSaveFilePath.Value = saveFilePath ?? string.Empty;
            if (Plugin.LastSlotIndex != null) Plugin.LastSlotIndex.Value = slotIndex;
            if (Plugin.LastCharacterId != null) Plugin.LastCharacterId.Value = characterId ?? string.Empty;
            if (Plugin.LastIsStoryMode != null) Plugin.LastIsStoryMode.Value = isStoryMode;
            if (Plugin.LastSaveWriteUtcTicks != null) Plugin.LastSaveWriteUtcTicks.Value = ticks;

            Plugin.Instance?.Config?.Save();
        }
    }
}
