using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.UI
{
    /// <summary>
    /// Controls pause menu visibility and time scale.
    /// Pressing Escape toggles the pause menu on/off.
    /// When paused, Time.timeScale is set to 0; when resumed, restored to 1.
    /// </summary>
    public class PauseMenuController : MonoBehaviour
    {
        [SerializeField] private GameObject menuPanel;

        private bool isPaused;
        private InputAction escapeAction;

        private void Awake()
        {
            escapeAction = new InputAction("Escape", InputActionType.Button, "<Keyboard>/escape");
            escapeAction.performed += OnEscapePressed;

            if (menuPanel != null)
                menuPanel.SetActive(false);

            isPaused = false;
        }

        private void OnEnable()
        {
            escapeAction.Enable();
        }

        private void OnDisable()
        {
            escapeAction.Disable();
        }

        private void OnDestroy()
        {
            escapeAction.performed -= OnEscapePressed;
            escapeAction.Dispose();
        }

        private void OnEscapePressed(InputAction.CallbackContext context)
        {
            if (isPaused)
                Resume();
            else
                Pause();
        }

        public void Pause()
        {
            isPaused = true;
            Time.timeScale = 0f;
            if (menuPanel != null)
                menuPanel.SetActive(true);
        }

        public void Resume()
        {
            isPaused = false;
            Time.timeScale = 1f;
            if (menuPanel != null)
                menuPanel.SetActive(false);
        }

        public void OnResumeClicked()
        {
            Resume();
        }

        public void OnSettingsClicked()
        {
            // Placeholder for settings panel navigation
        }

        public void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public bool IsPaused => isPaused;

        /// <summary>
        /// Static helper for bridge-driven testing. Finds the active instance and toggles pause.
        /// Returns JSON with the new state.
        /// </summary>
        public static string TogglePause()
        {
            var instance = FindObjectOfType<PauseMenuController>();
            if (instance == null)
                return "{\"success\":false,\"error\":\"PauseMenuController not found in scene\"}";

            if (instance.isPaused)
                instance.Resume();
            else
                instance.Pause();

            return string.Format("{{\"success\":true,\"isPaused\":{0},\"timeScale\":{1}}}",
                instance.isPaused ? "true" : "false", Time.timeScale);
        }

        public static string GetPauseState()
        {
            var instance = FindObjectOfType<PauseMenuController>();
            if (instance == null)
                return "{\"success\":false,\"error\":\"PauseMenuController not found in scene\"}";

            bool menuVisible = instance.menuPanel != null && instance.menuPanel.activeSelf;
            return string.Format("{{\"success\":true,\"isPaused\":{0},\"menuVisible\":{1},\"timeScale\":{2}}}",
                instance.isPaused ? "true" : "false",
                menuVisible ? "true" : "false",
                Time.timeScale);
        }
    }
}
