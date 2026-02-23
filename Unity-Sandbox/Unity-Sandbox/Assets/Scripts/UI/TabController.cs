using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public class TabController : MonoBehaviour
    {
        [SerializeField] private Button[] tabButtons;
        [SerializeField] private GameObject[] tabPanels;
        [SerializeField] private Color normalColor = new Color(0.25f, 0.25f, 0.25f);
        [SerializeField] private Color selectedColor = new Color(0.129f, 0.588f, 0.953f);

        private void Awake()
        {
            for (int i = 0; i < tabButtons.Length; i++)
            {
                int index = i;
                tabButtons[i].onClick.AddListener(() => SelectTab(index));
            }
            SelectTab(0);
        }

        public void SelectTab(int index)
        {
            for (int i = 0; i < tabButtons.Length; i++)
            {
                var img = tabButtons[i].GetComponent<Image>();
                if (img != null)
                    img.color = i == index ? selectedColor : normalColor;
            }
            for (int i = 0; i < tabPanels.Length; i++)
            {
                tabPanels[i].SetActive(i == index);
            }
        }
    }
}
