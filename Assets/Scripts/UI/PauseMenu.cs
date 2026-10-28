using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Roguelike
{
    /// <summary>
    /// Pause menu opened with Escape during a session: resume, switch between Game and Benchmark mode,
    /// go back to the main menu or quit. Built at runtime on the scene's Canvas, so it needs no setup in the scene.
    /// It cannot open while another screen already pauses the game (main menu, level up choice, end screen).
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        private const float ButtonWidth = 520f;
        private const float ButtonHeight = 80f;
        private const float ButtonSpacing = 20f;

        private GameObject mPanel;
        private TextMeshProUGUI mSwitchModeLabel;
        private TextMeshProUGUI mVSyncLabel;
        private bool mIsOpen;

        /// <summary>
        /// True while the pause menu is open.
        /// </summary>
        public static bool IsOpen { get; private set; }

        /// <summary>
        /// Creates the pause menu on the scene's root Canvas if the scene does not have one yet.
        /// </summary>
        public static void EnsureExists()
        {
            if (FindAnyObjectByType<PauseMenu>() != null) return;

            Canvas lCanvas = null;
            foreach (var lCandidate in FindObjectsByType<Canvas>())
            {
                if (!lCandidate.isRootCanvas) continue;
                if (lCanvas == null || lCandidate.renderMode == RenderMode.ScreenSpaceOverlay) lCanvas = lCandidate;
            }

            if (lCanvas == null)
            {
                Debug.LogWarning("PauseMenu: no Canvas found in the scene.");
                return;
            }

            var lMenuObject = new GameObject("[PauseMenu]", typeof(RectTransform));
            var lRect = (RectTransform)lMenuObject.transform;
            lRect.SetParent(lCanvas.transform, false);
            lRect.anchorMin = Vector2.zero;
            lRect.anchorMax = Vector2.one;
            lRect.offsetMin = Vector2.zero;
            lRect.offsetMax = Vector2.zero;

            // Last sibling: drawn on top of the HUD and of the other panels
            lRect.SetAsLastSibling();
            lMenuObject.AddComponent<PauseMenu>();
        }

        private void Awake()
        {
            BuildPanel();
            mPanel.SetActive(false);
        }

        private void Update()
        {
            var lKeyboard = Keyboard.current;
            if (lKeyboard == null || !lKeyboard.escapeKey.wasPressedThisFrame) return;

            if (mIsOpen)
            {
                Close();
            }
            else if (CanOpen())
            {
                Open();
            }
        }

        private void OnDestroy()
        {
            if (mIsOpen) IsOpen = false;
        }

        /// <summary>
        /// The menu only opens during a running session, when no other screen pauses the game.
        /// </summary>
        private static bool CanOpen()
        {
            return GameManager.Instance != null && GameManager.Instance.IsInSession
                && (LevelUpManager.Instance == null || !LevelUpManager.Instance.IsChoosingUpgrade);
        }

        private void Open()
        {
            mIsOpen = true;
            IsOpen = true;
            mSwitchModeLabel.text = GameManager.IsBenchmarkMode ? "Switch to Game mode" : "Switch to Benchmark mode";
            RefreshVSyncLabel();
            mPanel.SetActive(true);
            Time.timeScale = 0f;
        }

        private void Close()
        {
            mIsOpen = false;
            IsOpen = false;
            mPanel.SetActive(false);
            Time.timeScale = 1f;
        }

        /// <summary>
        /// Builds the dimmed full-screen panel, its title and its buttons.
        /// </summary>
        private void BuildPanel()
        {
            mPanel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var lPanelRect = (RectTransform)mPanel.transform;
            lPanelRect.SetParent(transform, false);
            lPanelRect.anchorMin = Vector2.zero;
            lPanelRect.anchorMax = Vector2.one;
            lPanelRect.offsetMin = Vector2.zero;
            lPanelRect.offsetMax = Vector2.zero;
            mPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);

            var lTitle = CreateText(lPanelRect, "PAUSE", 96, new Vector2(0f, 300f), new Vector2(800f, 140f));
            lTitle.fontStyle = FontStyles.Bold;

            CreateButton(lPanelRect, "Resume", 0, Close);
            mSwitchModeLabel = CreateButton(lPanelRect, "Switch mode", 1,
                () => GameManager.Instance.RestartInMode(!GameManager.IsBenchmarkMode));
            CreateButton(lPanelRect, "Main menu", 2, () => GameManager.Instance.ReturnToMainMenu());
            mVSyncLabel = CreateButton(lPanelRect, "V-Sync", 3, ToggleVSync);
            CreateButton(lPanelRect, "Quit game", 4, () => GameManager.Instance.QuitGame());
        }

        /// <summary>
        /// Switches the V-Sync setting of the Game mode, saved for the next sessions and applied immediately.
        /// </summary>
        private void ToggleVSync()
        {
            GameSettings.VSync = !GameSettings.VSync;
            GameSettings.ApplyFrameRate(GameManager.IsBenchmarkMode);
            RefreshVSyncLabel();
        }

        /// <summary>
        /// Shows the V-Sync setting; benchmarks are always uncapped, whatever the setting.
        /// </summary>
        private void RefreshVSyncLabel()
        {
            mVSyncLabel.text = GameManager.IsBenchmarkMode ? "V-Sync: Off (benchmark)"
                : GameSettings.VSync ? "V-Sync: On" : "V-Sync: Off";
        }

        /// <summary>
        /// Creates a button of the vertical list, centered on screen.
        /// </summary>
        /// <param name="pParent">Panel holding the buttons.</param>
        /// <param name="pLabel">Text of the button.</param>
        /// <param name="pIndex">Position in the list, from the top.</param>
        /// <param name="pOnClick">Action run when the button is clicked.</param>
        /// <returns>The label of the button, to change its text later.</returns>
        private TextMeshProUGUI CreateButton(RectTransform pParent, string pLabel, int pIndex,
            UnityEngine.Events.UnityAction pOnClick)
        {
            var lButtonObject = new GameObject(pLabel, typeof(RectTransform), typeof(Image), typeof(Button));
            var lRect = (RectTransform)lButtonObject.transform;
            lRect.SetParent(pParent, false);
            lRect.anchorMin = lRect.anchorMax = lRect.pivot = new Vector2(0.5f, 0.5f);
            lRect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            lRect.anchoredPosition = new Vector2(0f, 120f - pIndex * (ButtonHeight + ButtonSpacing));

            var lImage = lButtonObject.GetComponent<Image>();
            lImage.color = Color.white;

            var lButton = lButtonObject.GetComponent<Button>();
            lButton.targetGraphic = lImage;
            var lColors = lButton.colors;
            lColors.normalColor = new Color(0.16f, 0.18f, 0.24f, 0.95f);
            lColors.highlightedColor = new Color(0.28f, 0.32f, 0.42f, 1f);
            lColors.pressedColor = new Color(0.4f, 0.45f, 0.58f, 1f);
            lColors.selectedColor = lColors.highlightedColor;
            lButton.colors = lColors;
            lButton.onClick.AddListener(pOnClick);

            var lLabel = CreateText(lRect, pLabel, 40, Vector2.zero, lRect.sizeDelta);
            return lLabel;
        }

        /// <summary>
        /// Creates a centered white text.
        /// </summary>
        private static TextMeshProUGUI CreateText(RectTransform pParent, string pText, float pFontSize,
            Vector2 pPosition, Vector2 pSize)
        {
            var lTextObject = new GameObject("Text", typeof(RectTransform));
            var lRect = (RectTransform)lTextObject.transform;
            lRect.SetParent(pParent, false);
            lRect.anchorMin = lRect.anchorMax = lRect.pivot = new Vector2(0.5f, 0.5f);
            lRect.sizeDelta = pSize;
            lRect.anchoredPosition = pPosition;

            var lText = lTextObject.AddComponent<TextMeshProUGUI>();
            lText.text = pText;
            lText.fontSize = pFontSize;
            lText.alignment = TextAlignmentOptions.Center;
            lText.color = Color.white;
            lText.raycastTarget = false;
            return lText;
        }
    }
}
