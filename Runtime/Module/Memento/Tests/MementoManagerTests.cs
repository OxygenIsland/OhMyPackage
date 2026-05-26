// ============================================================
//  MementoManager — 单元测试
//  使用 Unity Test Framework (NUnit) 运行
//  路径：Assets/Tests/MementoManagerTests.cs
// ============================================================
using System;
using NUnit.Framework;

namespace OhMyPackage.MementoManager.Tests
{
    // ── 测试用 State DTO ──────────────────────────────────────
    [Serializable]
    public class TestState
    {
        public int    value;
        public string label;
    }

    // ── 测试用 Originator（纯 C# 非 MonoBehaviour）────────────
    public class TestOriginator : MementoOriginatorBase<TestState>
    {
        public int    Value { get; set; }
        public string Label { get; set; } = "";

        public override TestState CaptureState()  => new TestState { value = Value, label = Label };
        public override void      ApplyState(TestState s) { Value = s.value; Label = s.label; }
    }

    // ── 测试套件 ──────────────────────────────────────────────
    public class HistoryChannelTests
    {
        private HistoryChannel _ch;

        [SetUp]
        public void Setup()
        {
            _ch = new HistoryChannel("test", maxCapacity: 5);
        }

        private IMemento MakeMemento(int v, string label = "")
        {
            var m = new Memento<TestState>("test", label, new TestState { value = v }, v);
            return m;
        }

        [Test]
        public void Push_ShouldIncrementCount()
        {
            _ch.Push(MakeMemento(1));
            _ch.Push(MakeMemento(2));
            Assert.AreEqual(2, _ch.Count);
        }

        [Test]
        public void Undo_ShouldReturnPreviousMemento()
        {
            _ch.Push(MakeMemento(1, "step1"));
            _ch.Push(MakeMemento(2, "step2"));

            var result = _ch.Undo();
            Assert.IsNotNull(result);
            Assert.AreEqual("step1", result.Label);
        }

        [Test]
        public void Undo_AtStart_ShouldReturnNull()
        {
            _ch.Push(MakeMemento(1));
            var r1 = _ch.Undo();   // 只有1条，不能再 Undo
            Assert.IsNull(r1);
        }

        [Test]
        public void Redo_AfterUndo_ShouldReturnNextMemento()
        {
            _ch.Push(MakeMemento(1, "v1"));
            _ch.Push(MakeMemento(2, "v2"));
            _ch.Undo();

            var result = _ch.Redo();
            Assert.IsNotNull(result);
            Assert.AreEqual("v2", result.Label);
        }

        [Test]
        public void NewPush_AfterUndo_ShouldDiscardFuture()
        {
            _ch.Push(MakeMemento(1));
            _ch.Push(MakeMemento(2));
            _ch.Push(MakeMemento(3));
            _ch.Undo(); // cursor → index 1
            _ch.Undo(); // cursor → index 0

            _ch.Push(MakeMemento(99, "new"));  // 应丢弃 index 1,2

            Assert.AreEqual(2, _ch.Count);     // index 0 (v1) + index 1 (new)
            Assert.IsFalse(_ch.CanRedo);
        }

        [Test]
        public void MaxCapacity_ShouldDropOldest()
        {
            for (int i = 1; i <= 6; i++)       // 超过 capacity=5
                _ch.Push(MakeMemento(i));

            Assert.AreEqual(5, _ch.Count);     // 最旧的被丢弃
        }

        [Test]
        public void JumpToVersion_ShouldMoveCursor()
        {
            _ch.Push(MakeMemento(1));
            _ch.Push(MakeMemento(2));
            _ch.Push(MakeMemento(3));

            var result = _ch.JumpToVersion(2);
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Version);
        }

        [Test]
        public void Clear_ShouldResetEverything()
        {
            _ch.Push(MakeMemento(1));
            _ch.Push(MakeMemento(2));
            _ch.Clear();

            Assert.AreEqual(0,  _ch.Count);
            Assert.IsFalse(_ch.CanUndo);
            Assert.IsFalse(_ch.CanRedo);
            Assert.IsNull(_ch.Current());
        }
    }

    public class StateSerializerTests
    {
        [Test]
        public void DeepCopy_ShouldReturnIndependentCopy()
        {
            var original = new TestState { value = 42, label = "hello" };
            var copy     = StateSerializer.DeepCopy(original);

            copy.value  = 99;
            copy.label  = "world";

            // 原对象不受影响
            Assert.AreEqual(42,      original.value);
            Assert.AreEqual("hello", original.label);
        }

        [Test]
        public void SerializeDeserialize_ShouldRoundtrip()
        {
            var s    = new TestState { value = 7, label = "test" };
            var json = StateSerializer.Serialize(s);
            var back = StateSerializer.Deserialize<TestState>(json);

            Assert.AreEqual(s.value, back.value);
            Assert.AreEqual(s.label, back.label);
        }
    }
}
