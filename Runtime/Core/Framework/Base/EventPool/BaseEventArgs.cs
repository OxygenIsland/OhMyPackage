//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://OhMyPackage.cn/
// Feedback: mailto:ellan@OhMyPackage.cn
//------------------------------------------------------------

namespace OhMyPackage
{
    /// <summary>
    /// 事件基类。
    /// </summary>
    public abstract class BaseEventArgs : OhMyPackageEventArgs
    {
        /// <summary>
        /// 获取类型编号。
        /// </summary>
        public abstract int Id
        {
            get;
        }
    }
}
