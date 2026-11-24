using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZedZone.QuickContinue
{
    [HarmonyPatch(typeof(MainMenuController), nameof(MainMenuController.Start))]
    internal static class Patch_MainMenuController_Start
    {
        [HarmonyPostfix]
        private static void Postfix(MainMenuController __instance)
        {
            ContinueMenuLogic.Attach(__instance);
        }
    }

    [HarmonyPatch(typeof(MultiplayerPanel), nameof(MultiplayerPanel.TryInjectMenuButton))]
    internal static class Patch_MultiplayerPanel_TryInjectMenuButton
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            ContinueMenuLogic.NotifyMultiplayerButtonChecked();
        }
    }

    [HarmonyPatch(typeof(WorldEntry), nameof(WorldEntry.Enter))]
    internal static class Patch_WorldEntry_Enter
    {
        [HarmonyPostfix]
        private static void Postfix(GameData world, string characterId, bool autoHost, bool __result)
        {
            if (__result && !autoHost && world != null)
                ContinueSaveStore.RecordSinglePlayerEntry(world, characterId);
        }
    }

    [HarmonyPatch(typeof(GameDataManager), nameof(GameDataManager.SaveGameData))]
    internal static class Patch_GameDataManager_SaveGameData
    {
        [HarmonyPostfix]
        private static void Postfix(GameData gameData, int saveslotIndex, bool __result)
        {
            if (__result && gameData != null)
                ContinueSaveStore.RecordSinglePlayerSave(gameData, saveslotIndex);
        }
    }

    /// <summary>
    /// 挂在主菜单控制器上的轻量驱动组件，负责等待“联机游戏”按钮就绪并持续维护“继续游戏”按钮位置与多语言文本。
    /// </summary>
    public sealed class QuickContinueMenuDriver : MonoBehaviour
    {
        public QuickContinueMenuDriver(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            ContinueMenuLogic.Tick(this);
        }

        private void OnDestroy()
        {
            ContinueMenuLogic.OnDriverDestroyed(this);
        }
    }

    internal static class ContinueMenuLogic
    {
        private const string MenuButtonsRootName = "MainMenuButtons";
        private const string MultiplayerButtonName = "MultiplayerButton";
        private const string ContinueButtonName = "QuickContinueButton";
        private const int FallbackWaitFrames = 15;

        private static QuickContinueMenuDriver s_activeDriver;
        private static GameObject s_buttonObj;
        private static Button s_button;
        private static RectTransform s_buttonRect;
        private static RectTransform s_anchorRect;
        private static bool s_anchorIsMultiplayer;
        private static Text s_labelText;
        private static string s_lastLang;
        private static float s_nextPollTime;
        private static int s_framesSinceAttach;
        private static bool s_checkedNoSaves;
        private static bool s_isEntering;

        internal static void Attach(MainMenuController controller)
        {
            ResetState();
            if (controller == null || controller.gameObject == null) return;

            var driver = controller.gameObject.GetComponent<QuickContinueMenuDriver>();
            if (driver == null)
                driver = controller.gameObject.AddComponent<QuickContinueMenuDriver>();

            s_activeDriver = driver;
        }

        internal static void NotifyMultiplayerButtonChecked()
        {
            s_nextPollTime = 0f;
        }

        internal static void OnDriverDestroyed(QuickContinueMenuDriver driver)
        {
            if (driver != s_activeDriver) return;
            ResetState();
        }

        internal static void Tick(QuickContinueMenuDriver driver)
        {
            if (driver != s_activeDriver) return;
            if (Plugin.Enabled != null && !Plugin.Enabled.Value) return;

            if (s_buttonObj == null)
            {
                TryCreateContinueButton();
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < s_nextPollTime) return;
            s_nextPollTime = now + 0.5f;

            if (!s_anchorIsMultiplayer || s_anchorRect == null)
            {
                var root = GameObject.Find(MenuButtonsRootName);
                if (root != null)
                {
                    var mpTr = root.transform.Find(MultiplayerButtonName);
                    if (mpTr != null)
                    {
                        var mpRt = mpTr.GetComponent<RectTransform>();
                        if (mpRt != null)
                        {
                            s_anchorRect = mpRt;
                            s_anchorIsMultiplayer = true;
                        }
                    }
                }
            }

            if (s_buttonRect != null && s_anchorRect != null)
            {
                float spacing = GetSpacing();
                if (Mathf.Abs(s_buttonRect.anchoredPosition.x - s_anchorRect.anchoredPosition.x) > 0.5f ||
                    Mathf.Abs(s_buttonRect.anchoredPosition.y - (s_anchorRect.anchoredPosition.y + spacing)) > 0.5f)
                {
                    SyncButtonPosition(s_buttonRect, s_anchorRect);
                }
            }

            string lang = ReadCurrentLanguageCode();
            if (!string.Equals(lang, s_lastLang, StringComparison.OrdinalIgnoreCase) && s_labelText != null)
            {
                s_lastLang = lang;
                s_labelText.text = ResolveLocalizedLabel(lang);
            }
        }

        private static void TryCreateContinueButton()
        {
            if (s_checkedNoSaves) return;

            s_framesSinceAttach++;
            var root = GameObject.Find(MenuButtonsRootName);
            if (root == null) return;

            if (GameDataManager.instance == null && s_framesSinceAttach < 180)
                return;

            var mpTr = root.transform.Find(MultiplayerButtonName);
            if (mpTr == null && s_framesSinceAttach < FallbackWaitFrames)
                return;

            s_checkedNoSaves = true;
            if (!ContinueSaveStore.HasAnySinglePlayerSave())
            {
                Plugin.LogVerbose("No singleplayer saves detected; skipping QuickContinue button.");
                return;
            }

            var existing = root.transform.Find(ContinueButtonName);
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);

            GameObject templateGo = mpTr != null ? mpTr.gameObject : FindTopButtonTemplate(root.transform);
            if (templateGo == null) return;

            var clone = UnityEngine.Object.Instantiate(templateGo, root.transform);
            if (clone == null) return;

            clone.name = ContinueButtonName;
            clone.transform.localScale = templateGo.transform.localScale;
            clone.SetActive(true);

            var langTexts = clone.GetComponentsInChildren<LanguageText>(true);
            for (int i = 0; i < langTexts.Length; i++)
            {
                if (langTexts[i] != null)
                    UnityEngine.Object.DestroyImmediate(langTexts[i]);
            }

            var btn = clone.GetComponent<Button>();
            if (btn == null)
            {
                UnityEngine.Object.DestroyImmediate(clone);
                return;
            }

            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener((UnityAction)OnContinueClicked);
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.interactable = true;

            string lang = ReadCurrentLanguageCode();
            var label = clone.GetComponentInChildren<Text>(true);
            if (label != null)
                label.text = ResolveLocalizedLabel(lang);

            var anchorRt = templateGo.GetComponent<RectTransform>();
            var btnRt = clone.GetComponent<RectTransform>();
            SyncButtonPosition(btnRt, anchorRt);

            s_buttonObj = clone;
            s_button = btn;
            s_buttonRect = btnRt;
            s_anchorRect = anchorRt;
            s_anchorIsMultiplayer = mpTr != null;
            s_labelText = label;
            s_lastLang = lang;
            s_nextPollTime = Time.realtimeSinceStartup + 0.5f;

            Plugin.LogVerbose($"Injected {ContinueButtonName} above {templateGo.name} (lang={lang}).");
        }

        private static GameObject FindTopButtonTemplate(Transform root)
        {
            GameObject bestGo = null;
            float bestY = float.NegativeInfinity;

            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == null || !child.gameObject.activeSelf) continue;
                if (string.Equals(child.name, ContinueButtonName, StringComparison.Ordinal)) continue;
                if (child.GetComponent<Button>() == null) continue;

                var rt = child.GetComponent<RectTransform>();
                if (rt == null) continue;

                if (bestGo == null || rt.anchoredPosition.y > bestY)
                {
                    bestGo = child.gameObject;
                    bestY = rt.anchoredPosition.y;
                }
            }

            return bestGo;
        }

        private static void OnContinueClicked()
        {
            if (s_isEntering) return;

            ClearUiSelection();

            if (ContinueSaveStore.IsMultiplayerActive())
            {
                Plugin.LogVerbose("Blocked QuickContinue click while multiplayer lobby/session is active.");
                return;
            }

            s_isEntering = true;
            if (s_button != null)
                s_button.interactable = false;

            if (!ContinueSaveStore.TryResolveTarget(out var target))
            {
                Plugin.Logs?.LogWarning("QuickContinue could not find any readable singleplayer save to load.");
                RestoreButtonReady();
                if (s_buttonObj != null && !ContinueSaveStore.HasAnySinglePlayerSave())
                    s_buttonObj.SetActive(false);
                return;
            }

            string mapName = target.World.mapConfigData != null
                ? (target.World.mapConfigData.mapName ?? target.World.id ?? "")
                : (target.World.id ?? "");
            Plugin.Logs?.LogInfo(
                $"QuickContinue entering singleplayer: map={mapName}, slot={target.World.saveSlotIndex}, " +
                $"story={target.IsStoryMode}, char={(string.IsNullOrEmpty(target.CharacterId) ? "<none>" : target.CharacterId)}");

            ContinueSaveStore.TryEnterTarget(target, RestoreButtonReady);
        }

        private static void RestoreButtonReady()
        {
            s_isEntering = false;
            if (s_button != null)
                s_button.interactable = true;
        }

        private static void SyncButtonPosition(RectTransform buttonRt, RectTransform anchorRt)
        {
            if (buttonRt == null || anchorRt == null) return;
            buttonRt.anchoredPosition = new Vector2(anchorRt.anchoredPosition.x, anchorRt.anchoredPosition.y + GetSpacing());
        }

        private static float GetSpacing()
        {
            return Plugin.VerticalSpacing != null ? Plugin.VerticalSpacing.Value : 26f;
        }

        private static string ReadCurrentLanguageCode()
        {
            try
            {
                string code = SinglePlayerModePanel.CurrentLanguageCode();
                if (!string.IsNullOrEmpty(code))
                    return code;
            }
            catch
            {
                // 回退 Unity 系统语言检测
            }

            return Application.systemLanguage switch
            {
                SystemLanguage.ChineseSimplified or SystemLanguage.Chinese => "zh-CN",
                SystemLanguage.ChineseTraditional => "zh-TW",
                SystemLanguage.Japanese => "ja",
                SystemLanguage.Korean => "ko",
                SystemLanguage.Russian => "ru",
                _ => "en",
            };
        }

        private static string ResolveLocalizedLabel(string langCode)
        {
            if (string.IsNullOrEmpty(langCode)) return "Continue";

            string lower = langCode.Trim().ToLowerInvariant();
            if (lower.StartsWith("zh-tw") || lower.StartsWith("zh-hk") || lower.StartsWith("zh_tw") || lower.StartsWith("zh_hk") || lower == "cht")
                return "繼續遊戲";
            if (lower.StartsWith("zh"))
                return "继续游戏";
            if (lower.StartsWith("ja"))
                return "続きから";
            if (lower.StartsWith("ko"))
                return "이어하기";
            if (lower.StartsWith("ru"))
                return "Продолжить";
            return "Continue";
        }

        private static void ClearUiSelection()
        {
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null)
                es.SetSelectedGameObject(null);
        }

        private static void ResetState()
        {
            s_activeDriver = null;
            s_buttonObj = null;
            s_button = null;
            s_buttonRect = null;
            s_anchorRect = null;
            s_anchorIsMultiplayer = false;
            s_labelText = null;
            s_lastLang = null;
            s_nextPollTime = 0f;
            s_framesSinceAttach = 0;
            s_checkedNoSaves = false;
            s_isEntering = false;
        }
    }
}
