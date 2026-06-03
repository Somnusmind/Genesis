using UnityEngine;
using TMPro; 

public class MetaDescription : MonoBehaviour
{
    public TMP_Text metaDescriptionText;

    private string metaDescription;

    public void UpdateMetaDescription()
    {

        Config config = ConfigManager.Instance.LoadConfiguration();
        
        metaDescription = config.metaDescription;

        metaDescriptionText.text = metaDescription;

    }
}