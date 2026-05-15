//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://OhMyPackage.cn/
// Feedback: mailto:ellan@OhMyPackage.cn
//------------------------------------------------------------

namespace OhMyPackage
{
    public static partial class Version
    {
        /// <summary>
        /// 版本号辅助器接口。
        /// </summary>
        public interface IVersionHelper
        {
            /// <summary>
            /// 获取游戏版本号。
            /// </summary>
            string GameVersion
            {
                get;
            }

            /// <summary>
            /// 获取内部游戏版本号。
            /// </summary>
            int InternalGameVersion
            {
                get;
            }
        }
    }
}
