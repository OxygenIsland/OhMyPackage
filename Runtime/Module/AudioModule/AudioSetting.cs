using UnityEngine;

namespace GameFramework
{
    [CreateAssetMenu(menuName = "GameFramework/AudioSetting", fileName = "AudioSetting")]
    public class AudioSetting : ScriptableObject
    {
        public AudioGroupConfig[] audioGroupConfigs = null;
    }
}