using UnityEngine;

namespace OhMyPackage
{
    [CreateAssetMenu(menuName = "OhMyPackage/AudioSetting", fileName = "AudioSetting")]
    public class AudioSetting : ScriptableObject
    {
        public AudioGroupConfig[] audioGroupConfigs = null;
    }
}