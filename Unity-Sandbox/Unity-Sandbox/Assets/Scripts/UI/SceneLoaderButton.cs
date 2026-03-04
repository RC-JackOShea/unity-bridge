using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.UI
{
    public class SceneLoaderButton : MonoBehaviour
    {
        [SerializeField] private string sceneName;

        public void LoadScene()
        {
            if (!string.IsNullOrEmpty(sceneName))
                SceneManager.LoadScene(sceneName);
        }
    }
}
