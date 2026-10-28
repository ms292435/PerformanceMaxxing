using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Roguelike
{
    /// <summary>
    /// Adds the "How to play" and "High scores" screens to the main menu.
    /// The screens are built at runtime. Their buttons can be set up in the scene: add this component to the main
    /// menu panel and assign them, no OnClick needed. A button left empty is created at runtime as a copy of the
    /// menu's Benchmark button, under it.
    /// </summary>
    public class MainMenuScreens : MonoBehaviour
    {
        [Tooltip("Button of the main menu opening the How to play screen. Created at runtime if empty.")]
        [SerializeField] private Button mHelpButton;

        [Tooltip("Button of the main menu opening the High scores screen. Created at runtime if empty.")]
        [SerializeField] private Button mHighScoresButton;

        [Tooltip("Button of the main menu quitting the game. Created at runtime, as a cross in the top right corner, if empty.")]
        [SerializeField] private Button mQuitButton;

        private const float ButtonGap = 40f;
        private const float ScreenWidth = 3400f;
        private const float ScreenHeight = 1900f;
        private const float QuitButtonSize = 170f;
        private const float QuitButtonMargin = 60f;

        private Button mTemplateButton;
        private TMP_FontAsset mFont;
        private GameObject mHelpScreen;
        private GameObject mHighScoresScreen;
        private TextMeshProUGUI mHighScoresText;

        /// <summary>
        /// Adds the screens to the main menu panel, once.
        /// </summary>
        /// <param name="pMainMenuPanel">The main menu panel holding the Play and Benchmark buttons.</param>
        public static void EnsureExists(GameObject pMainMenuPanel)
        {
            if (pMainMenuPanel == null || pMainMenuPanel.GetComponent<MainMenuScreens>() != null) return;
            pMainMenuPanel.AddComponent<MainMenuScreens>();
        }

        private void Awake()
        {
            // The Benchmark button of the menu is the template of the buttons created at runtime
            Button[] lButtons = GetComponentsInChildren<Button>(true);
            if (lButtons.Length == 0)
            {
                Debug.LogWarning("MainMenuScreens: no button found in the main menu.");
                return;
            }
            mTemplateButton = System.Array.Find(lButtons, lButton => lButton.name == "Benchmark")
                              ?? lButtons[lButtons.Length - 1];
            var lTemplateLabel = mTemplateButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (lTemplateLabel != null) mFont = lTemplateLabel.font;

            var lTemplateRect = (RectTransform)mTemplateButton.transform;
            Vector2 lSize = new(lTemplateRect.sizeDelta.x - ButtonGap / 2f, lTemplateRect.sizeDelta.y * 0.45f);
            float lY = lTemplateRect.anchoredPosition.y - lTemplateRect.sizeDelta.y / 2f - ButtonGap - lSize.y / 2f;
            float lHalfOffset = lSize.x / 2f + ButtonGap / 2f;

            if (mHelpButton == null)
            {
                mHelpButton = CreateMenuButton("How to play",
                    new Vector2(lTemplateRect.anchoredPosition.x - lHalfOffset, lY), lSize);
            }
            if (mHighScoresButton == null)
            {
                mHighScoresButton = CreateMenuButton("High scores",
                    new Vector2(lTemplateRect.anchoredPosition.x + lHalfOffset, lY), lSize);
            }
            if (mQuitButton == null) mQuitButton = CreateQuitButton();
            mHelpButton.onClick.AddListener(() => Show(mHelpScreen));
            mHighScoresButton.onClick.AddListener(ShowHighScores);
            mQuitButton.onClick.AddListener(() => GameManager.Instance.QuitGame());

            mHelpScreen = CreateScreen("HOW TO PLAY", out var lHelpText);
            lHelpText.text = BuildHelpText();

            mHighScoresScreen = CreateScreen("HIGH SCORES", out mHighScoresText);
        }

        private void Update()
        {
            // Escape closes the open screen and goes back to the menu
            var lKeyboard = Keyboard.current;
            if (lKeyboard != null && lKeyboard.escapeKey.wasPressedThisFrame) CloseScreens();
        }

        private void OnDisable()
        {
            CloseScreens();
        }

        private void Show(GameObject pScreen)
        {
            CloseScreens();
            if (pScreen != null) pScreen.SetActive(true);
        }

        private void ShowHighScores()
        {
            mHighScoresText.text = BuildHighScoresText();
            Show(mHighScoresScreen);
        }

        private void CloseScreens()
        {
            if (mHelpScreen != null) mHelpScreen.SetActive(false);
            if (mHighScoresScreen != null) mHighScoresScreen.SetActive(false);
        }

        private static string BuildHelpText()
        {
            return
                "<size=115%><b>Controls</b></size>\n" +
                "Move: W A S D (Z Q S D on AZERTY)\n" +
                "Pause: Escape\n\n" +
                "<size=115%><b>Goal</b></size>\n" +
                "Survive 5 minutes, then defeat the final boss.\n" +
                "Weapons fire on their own: move and dodge.\n\n" +
                "<size=115%><b>Progression</b></size>\n" +
                "Collect experience to level up and choose an upgrade.\n" +
                "Level 7 on any weapon unlocks a mastery bonus.\n" +
                "Pickups: Heal, Magnet, Bomb.\n\n" +
                "<size=115%><b>Benchmark mode</b></size>\n" +
                "Stress test, up to 1,000,000 enemies.\n" +
                "1-7 presets  +/- adjust  C clear  F1 hide panel";
        }

        private static string BuildHighScoresText()
        {
            var lEntries = HighScores.Entries;
            if (lEntries.Count == 0)
            {
                return "\n<align=center>No run recorded yet.\nPlay a game to set the first high score!</align>";
            }

            var lBuilder = new StringBuilder();
            lBuilder.Append("<b>#<pos=8%>Kills<pos=28%>Time<pos=46%>Level<pos=62%>Result<pos=80%>Date</b>\n");
            for (int i = 0; i < lEntries.Count; i++)
            {
                HighScoreEntry lEntry = lEntries[i];
                int lSeconds = Mathf.FloorToInt(lEntry.mTime);
                string lResult = lEntry.mVictory ? "<color=#FFD24D>Victory</color>" : "Defeat";
                lBuilder.Append($"{i + 1}<pos=8%>{lEntry.mKills:N0}<pos=28%>{lSeconds / 60}:{lSeconds % 60:00}" +
                                $"<pos=46%>{lEntry.mLevel}<pos=62%>{lResult}<pos=80%>{lEntry.mDate}\n");
            }
            return lBuilder.ToString();
        }

        /// <summary>
        /// Creates a menu button as a copy of the template button, with its own label and no action.
        /// </summary>
        /// <returns>The new button.</returns>
        private Button CreateMenuButton(string pLabel, Vector2 pPosition, Vector2 pSize)
        {
            Button lButton = Instantiate(mTemplateButton, transform);
            lButton.name = pLabel;

            var lRect = (RectTransform)lButton.transform;
            lRect.anchoredPosition = pPosition;
            lRect.sizeDelta = pSize;

            // Drop the template's action (start a benchmark)
            lButton.onClick = new Button.ButtonClickedEvent();

            var lLabel = lButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (lLabel != null)
            {
                lLabel.text = pLabel;
                lLabel.enableAutoSizing = true;
                lLabel.fontSizeMin = 20f;
                lLabel.fontSizeMax = lLabel.fontSize * 0.7f;
            }
            return lButton;
        }

        /// <summary>
        /// Creates the quit button: a red square with a cross, in the top right corner of the menu.
        /// </summary>
        /// <returns>The new button, with no action.</returns>
        private Button CreateQuitButton()
        {
            var lButtonObject = new GameObject("Quit", typeof(RectTransform), typeof(Image), typeof(Button));
            var lRect = (RectTransform)lButtonObject.transform;
            lRect.SetParent(transform, false);
            lRect.anchorMin = lRect.anchorMax = lRect.pivot = Vector2.one;
            lRect.anchoredPosition = new Vector2(-QuitButtonMargin, -QuitButtonMargin);
            lRect.sizeDelta = new Vector2(QuitButtonSize, QuitButtonSize);

            var lImage = lButtonObject.GetComponent<Image>();
            lImage.color = Color.white;

            var lButton = lButtonObject.GetComponent<Button>();
            lButton.targetGraphic = lImage;
            var lColors = lButton.colors;
            lColors.normalColor = new Color(0.62f, 0.16f, 0.14f, 1f);
            lColors.highlightedColor = new Color(0.82f, 0.24f, 0.2f, 1f);
            lColors.pressedColor = new Color(0.45f, 0.1f, 0.09f, 1f);
            lColors.selectedColor = lColors.normalColor;
            lButton.colors = lColors;

            var lCross = CreateText(lRect, "X", 120f, TextAlignmentOptions.Center);
            SetRect(lCross.rectTransform, Vector2.zero, lRect.sizeDelta);
            lCross.fontStyle = FontStyles.Bold;
            return lButton;
        }

        /// <summary>
        /// Creates a hidden full-screen page: a dimmed background, a box with a title, a body text and a Back button.
        /// </summary>
        /// <param name="pTitle">Title of the page.</param>
        /// <param name="pBody">Body text of the page, filled by the caller.</param>
        /// <returns>The page, hidden.</returns>
        private GameObject CreateScreen(string pTitle, out TextMeshProUGUI pBody)
        {
            var lScreen = new GameObject(pTitle, typeof(RectTransform), typeof(Image));
            var lScreenRect = (RectTransform)lScreen.transform;
            lScreenRect.SetParent(transform, false);
            lScreenRect.anchorMin = Vector2.zero;
            lScreenRect.anchorMax = Vector2.one;
            lScreenRect.offsetMin = Vector2.zero;
            lScreenRect.offsetMax = Vector2.zero;
            lScreen.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 1f);

            var lBox = new GameObject("Box", typeof(RectTransform), typeof(Image));
            var lBoxRect = (RectTransform)lBox.transform;
            lBoxRect.SetParent(lScreenRect, false);
            lBoxRect.sizeDelta = new Vector2(ScreenWidth, ScreenHeight);
            lBox.GetComponent<Image>().color = new Color(0.1f, 0.11f, 0.16f, 1f);

            var lTitle = CreateText(lBoxRect, pTitle, 130f, TextAlignmentOptions.Center);
            SetRect(lTitle.rectTransform, new Vector2(0f, ScreenHeight / 2f - 140f), new Vector2(ScreenWidth, 180f));
            lTitle.fontStyle = FontStyles.Bold;

            // The body shrinks its font to always fit between the title and the Back button
            pBody = CreateText(lBoxRect, string.Empty, 56f, TextAlignmentOptions.TopLeft);
            SetRect(pBody.rectTransform, new Vector2(0f, -10f), new Vector2(ScreenWidth - 240f, ScreenHeight - 560f));
            pBody.enableAutoSizing = true;
            pBody.fontSizeMin = 20f;
            pBody.fontSizeMax = 56f;
            pBody.lineSpacing = 10f;

            // Back button, a copy of the menu buttons
            Button lBack = Instantiate(mTemplateButton, lBoxRect);
            lBack.name = "Back";
            lBack.onClick = new Button.ButtonClickedEvent();
            lBack.onClick.AddListener(CloseScreens);
            SetRect((RectTransform)lBack.transform, new Vector2(0f, -ScreenHeight / 2f + 130f), new Vector2(600f, 170f));
            var lBackLabel = lBack.GetComponentInChildren<TextMeshProUGUI>(true);
            if (lBackLabel != null)
            {
                lBackLabel.text = "Back";
                lBackLabel.enableAutoSizing = true;
                lBackLabel.fontSizeMin = 20f;
                lBackLabel.fontSizeMax = 90f;
            }

            lScreen.SetActive(false);
            return lScreen;
        }

        private TextMeshProUGUI CreateText(RectTransform pParent, string pText, float pFontSize,
            TextAlignmentOptions pAlignment)
        {
            var lTextObject = new GameObject("Text", typeof(RectTransform));
            lTextObject.transform.SetParent(pParent, false);

            var lText = lTextObject.AddComponent<TextMeshProUGUI>();
            if (mFont != null) lText.font = mFont;
            lText.text = pText;
            lText.fontSize = pFontSize;
            lText.alignment = pAlignment;
            lText.color = Color.white;
            lText.richText = true;
            lText.raycastTarget = false;
            return lText;
        }

        private static void SetRect(RectTransform pRect, Vector2 pPosition, Vector2 pSize)
        {
            pRect.anchorMin = pRect.anchorMax = pRect.pivot = new Vector2(0.5f, 0.5f);
            pRect.anchoredPosition = pPosition;
            pRect.sizeDelta = pSize;
        }
    }
}
