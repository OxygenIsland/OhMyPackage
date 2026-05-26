// ============================================================
//  MementoManager — StateSerializer
//  基于 Unity JsonUtility 的深拷贝（Deep Copy）方案
//  调研来源：
//    github.com OscarAbraham/PlainDataInstancer — JsonUtility 深克隆
//    medium.com krmsrn — JsonUtility vs ScriptableObject 性能对比
//
//  选型说明：
//    · JsonUtility 序列化速度最快（Unity 原生），无需第三方库
//    · 只支持带 [Serializable] 的纯 C# 数据类（State DTO）
//    · 不支持 UnityEngine.Object 引用（设计上应该把引用转为 ID）
// ============================================================
using System;
using UnityEngine;

namespace OhMyPackage.MementoManager
{
    internal static class StateSerializer
    {
        /// <summary>
        /// 通过 JSON 序列化/反序列化实现深拷贝，保证快照与原对象完全隔离。
        /// State 类必须标注 [System.Serializable]。
        /// </summary>
        internal static TState DeepCopy<TState>(TState source) where TState : class, new()
        {
            if (source == null) return new TState();

            // JsonUtility 是 Unity 内置，IL2CPP 友好，无反射开销
            string json  = JsonUtility.ToJson(source);
            TState copy  = JsonUtility.FromJson<TState>(json);
            return copy;
        }

        /// <summary>
        /// 把 State 序列化为 JSON 字符串（用于持久化/调试）。
        /// </summary>
        internal static string Serialize<TState>(TState state) where TState : class, new()
            => JsonUtility.ToJson(state, prettyPrint: true);

        /// <summary>
        /// 从 JSON 字符串反序列化 State（用于持久化加载）。
        /// </summary>
        internal static TState Deserialize<TState>(string json) where TState : class, new()
            => JsonUtility.FromJson<TState>(json);
    }
}
