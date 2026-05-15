using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 全局配置管理器接口。
    /// <para>
    /// 支持从多个 <see cref="IConfigSource"/> 异步加载 JSON 配置，并以合并方式写入内部存储。
    /// 提供扁平键值访问（兼容原 GameFramework 风格）和强类型 Section 反序列化两种读取方式。
    /// </para>
    ///
    /// <example>
    /// <code>
    /// // 异步加载
    /// bool ok = await configManager.LoadAsync(new ResourcesConfigSource("Configs/Global"));
    ///
    /// // 扁平键值访问（兼容旧风格）
    /// bool musicOn  = configManager.GetBool("Music.Enable", true);
    /// int  maxHp    = configManager.GetInt("Player.MaxHP", 100);
    /// float speed   = configManager.GetFloat("Player.MoveSpeed", 5f);
    /// string server = configManager.GetString("Network.ServerUrl", "");
    ///
    /// // 强类型 Section 访问（推荐）
    /// var player = configManager.GetSection&lt;PlayerConfig&gt;("Player");
    /// var net    = configManager.GetSection&lt;NetworkConfig&gt;("Network");
    /// </code>
    /// </example>
    /// </summary>
    public interface IConfigManager
    {
        /// <summary>当前已加载的顶层配置条目数量。</summary>
        int Count { get; }

        /// <summary>
        /// 从指定数据源异步加载 JSON 配置，并将其内容**合并**到当前配置中。
        /// <para>若同名 Key 已存在，后加载的值会覆盖先前的值（Last-Write-Wins）。</para>
        /// </summary>
        /// <param name="source">配置数据源。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>加载成功返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        UniTask<bool> LoadAsync(IConfigSource source, CancellationToken cancellationToken = default);

        /// <summary>检查指定键是否存在于配置中。</summary>
        /// <param name="key">配置键。</param>
        bool HasConfig(string key);

        /// <summary>
        /// 将整个配置根对象反序列化为指定类型。
        /// </summary>
        /// <typeparam name="T">目标类型，需有无参构造函数。</typeparam>
        /// <returns>反序列化结果；若失败则返回 <c>new T()</c>。</returns>
        T GetSection<T>() where T : class, new();

        /// <summary>
        /// 将指定键下的嵌套 JSON 对象反序列化为指定类型。
        /// <para>适用于 JSON 中存在嵌套对象的场景：
        /// <code>{ "Player": { "MaxHP": 200, "MoveSpeed": 6.0 } }</code>
        /// </para>
        /// </summary>
        /// <typeparam name="T">目标类型，需有无参构造函数。</typeparam>
        /// <param name="sectionKey">嵌套对象的键名。</param>
        /// <returns>反序列化结果；若键不存在或解析失败则返回 <c>new T()</c>。</returns>
        T GetSection<T>(string sectionKey) where T : class, new();

        /// <summary>读取布尔配置值。</summary>
        /// <param name="key">配置键。</param>
        /// <param name="defaultValue">键不存在时返回的默认值。</param>
        bool GetBool(string key, bool defaultValue = false);

        /// <summary>读取整数配置值。</summary>
        /// <param name="key">配置键。</param>
        /// <param name="defaultValue">键不存在时返回的默认值。</param>
        int GetInt(string key, int defaultValue = 0);

        /// <summary>读取浮点配置值。</summary>
        /// <param name="key">配置键。</param>
        /// <param name="defaultValue">键不存在时返回的默认值。</param>
        float GetFloat(string key, float defaultValue = 0f);

        /// <summary>读取字符串配置值。</summary>
        /// <param name="key">配置键。</param>
        /// <param name="defaultValue">键不存在时返回的默认值。</param>
        string GetString(string key, string defaultValue = "");

        /// <summary>
        /// 在运行时动态写入或覆盖一个字符串配置项。
        /// <para>适用于调试或运行时动态参数注入场景。</para>
        /// </summary>
        /// <param name="key">配置键。</param>
        /// <param name="value">配置值（字符串形式）。</param>
        void Set(string key, string value);

        /// <summary>移除指定键的配置项。</summary>
        /// <param name="key">配置键。</param>
        /// <returns>键存在并成功移除返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        bool Remove(string key);

        /// <summary>清空所有已加载的配置数据。</summary>
        void Clear();

        /// <summary>
        /// 配置源加载成功时触发。
        /// <para>参数为加载成功的 <see cref="IConfigSource.SourceId"/>。</para>
        /// </summary>
        event Action<string> OnLoadSuccess;

        /// <summary>
        /// 配置源加载失败时触发。
        /// <para>参数依次为 <see cref="IConfigSource.SourceId"/> 和错误信息。</para>
        /// </summary>
        event Action<string, string> OnLoadFailure;
    }
}
