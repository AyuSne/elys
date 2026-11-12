using System.Collections.Generic;
using System.Text;
using ElysFarm.Core;
using ElysFarm.Demo;
using NUnit.Framework;
using UnityEngine;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #15 头顶表情：接单 / 被拒 / 去休息 / 转述四种事件各冒一个像素表情，
    /// 2.5 秒后消失、同时最多三个；表情只是反馈，不碰领域状态。
    /// 测试打 <see cref="EmoteBoard"/> 这条纯数据接缝，不打开场景、不加载贴图。
    /// </summary>
    public class OverheadEmotesTests
    {
        static readonly EmoteEvent[] AllEvents =
        {
            EmoteEvent.Accepted,
            EmoteEvent.Rejected,
            EmoteEvent.Resting,
            EmoteEvent.Retelling
        };

        [Test]
        public void 四种事件各冒一个表情_两秒半后消失()
        {
            foreach (var emote in AllEvents)
            {
                var board = new EmoteBoard();
                board.Post(FarmDemoBootstrap.AheId, emote, 10f);

                var active = board.ActiveAt(10f);
                Assert.AreEqual(1, active.Count, $"{emote} 应立刻冒出来");
                Assert.AreEqual(emote, active[0].Event, "冒的应是这件事对应的表情");
                Assert.AreEqual(FarmDemoBootstrap.AheId, active[0].NpcId);

                Assert.AreEqual(
                    1,
                    board.ActiveAt(10f + OverheadEmotes.Duration - 0.01f).Count,
                    $"{emote} 没到 2.5 秒不该消失");
                Assert.AreEqual(
                    0,
                    board.ActiveAt(10f + OverheadEmotes.Duration).Count,
                    $"{emote} 满 2.5 秒就该消失");
            }
        }

        [Test]
        public void 同时最多三个_第四张挤掉最早那张()
        {
            var board = new EmoteBoard();
            var ids = new[] { "a", "b", "c", "d" };
            for (var i = 0; i < ids.Length; i++)
            {
                board.Post(ids[i], AllEvents[i % AllEvents.Length], i);
            }

            var active = board.ActiveAt(3f);
            Assert.AreEqual(OverheadEmotes.MaxConcurrent, active.Count, "同时最多三个");
            Assert.AreEqual("b", active[0].NpcId, "最早的那张先被挤掉");
            Assert.AreEqual("d", active[active.Count - 1].NpcId, "最新的那张留着");
            Assert.IsFalse(board.TryGet("a", 3f, out _), "被挤掉的人不再有表情");
            Assert.IsTrue(board.TryGet("d", 3f, out var newest));
            Assert.AreEqual(EmoteEvent.Retelling, newest.Event);
        }

        [Test]
        public void 同一名NPC再冒表情会替换上一张()
        {
            var board = new EmoteBoard();
            board.Post(FarmDemoBootstrap.AheId, EmoteEvent.Accepted, 1f);
            board.Post(FarmDemoBootstrap.AheId, EmoteEvent.Rejected, 2f);

            var active = board.ActiveAt(2f);
            Assert.AreEqual(1, active.Count, "一个人头顶只留一张");
            Assert.AreEqual(EmoteEvent.Rejected, active[0].Event);
            Assert.AreEqual(2f, active[0].StartedAt, 0.0001f, "新表情重新计 2.5 秒");
        }

        [Test]
        public void 四种事件各用图集里不同的一行_每行四帧()
        {
            var rows = new HashSet<int>();
            foreach (var emote in AllEvents)
            {
                var row = OverheadEmotes.RowOf(emote);
                Assert.GreaterOrEqual(row, 0, $"{emote} 应有图集行");
                Assert.IsTrue(rows.Add(row), $"{emote} 不该与别的事件共用一行，四种事件要能分辨");

                for (var frame = 0; frame < OverheadEmotes.FrameCount; frame++)
                {
                    var cell = OverheadEmotes.CellOf(emote, frame);
                    Assert.IsTrue(cell.IsValid, $"{emote} 第 {frame} 帧应有格位");
                    Assert.AreEqual(frame, cell.Column, "列是帧号");
                    Assert.AreEqual(row, cell.Row, "行是事件");
                    Assert.AreEqual(16, cell.Width, "一格 16px");
                    Assert.AreEqual(16, cell.Height, "一格 16px");
                }
            }

            Assert.IsFalse(
                OverheadEmotes.CellOf(EmoteEvent.Accepted, -1).IsValid,
                "帧号不能为负");
            Assert.IsFalse(
                OverheadEmotes.CellOf(EmoteEvent.Accepted, OverheadEmotes.FrameCount).IsValid,
                "每种表情只有四帧");
            Assert.AreEqual(-1, OverheadEmotes.RowOf((EmoteEvent)99), "未知事件没有行");
            Assert.IsFalse(OverheadEmotes.CellOf((EmoteEvent)99, 0).IsValid, "未知事件不画");
        }

        [Test]
        public void 表情先弹四帧再定格()
        {
            Assert.AreEqual(0, OverheadEmotes.FrameAt(-1f), "还没冒出来时停在第一帧");
            Assert.AreEqual(0, OverheadEmotes.FrameAt(0f));
            Assert.AreEqual(1, OverheadEmotes.FrameAt(OverheadEmotes.SecondsPerFrame + 0.001f));
            Assert.AreEqual(2, OverheadEmotes.FrameAt(OverheadEmotes.SecondsPerFrame * 2f + 0.001f));
            Assert.AreEqual(3, OverheadEmotes.FrameAt(OverheadEmotes.SecondsPerFrame * 3f + 0.001f));
            Assert.AreEqual(3, OverheadEmotes.FrameAt(1f), "四帧播完停在最后一帧");
            Assert.AreEqual(3, OverheadEmotes.FrameAt(OverheadEmotes.Duration), "整个 2.5 秒里都有图");
        }

        [Test]
        public void NPC出屏或位于相机背后时不画()
        {
            var center = new Vector2(0f, 0f);
            const float halfWidth = 10f;
            const float halfHeight = 6f;
            const float cameraZ = -10f;

            Assert.IsTrue(
                OverheadEmotes.IsVisible(Vector3.zero, center, halfWidth, halfHeight, cameraZ),
                "画面正中可见");
            Assert.IsTrue(
                OverheadEmotes.IsVisible(new Vector3(10f, 6f, 0f), center, halfWidth, halfHeight, cameraZ),
                "视野四边仍算在画面里，别让表情在边上闪掉");
            Assert.IsFalse(
                OverheadEmotes.IsVisible(new Vector3(10.01f, 0f, 0f), center, halfWidth, halfHeight, cameraZ),
                "右出屏不画");
            Assert.IsFalse(
                OverheadEmotes.IsVisible(new Vector3(-10.01f, 0f, 0f), center, halfWidth, halfHeight, cameraZ),
                "左出屏不画");
            Assert.IsFalse(
                OverheadEmotes.IsVisible(new Vector3(0f, 6.01f, 0f), center, halfWidth, halfHeight, cameraZ),
                "上出屏不画");
            Assert.IsFalse(
                OverheadEmotes.IsVisible(new Vector3(0f, -6.01f, 0f), center, halfWidth, halfHeight, cameraZ),
                "下出屏不画");

            Assert.IsFalse(
                OverheadEmotes.IsVisible(new Vector3(0f, 0f, cameraZ), center, halfWidth, halfHeight, cameraZ),
                "贴着相机平面不算在相机前方");
            Assert.IsFalse(
                OverheadEmotes.IsVisible(new Vector3(0f, 0f, cameraZ - 0.01f), center, halfWidth, halfHeight, cameraZ),
                "相机背后不画");
            Assert.IsTrue(
                OverheadEmotes.IsVisible(new Vector3(0f, 0f, cameraZ + 0.01f), center, halfWidth, halfHeight, cameraZ),
                "相机前方一点点仍可见");

            var followCenter = new Vector2(20f, 0f);
            Assert.IsFalse(
                OverheadEmotes.IsVisible(Vector3.zero, followCenter, halfWidth, halfHeight, cameraZ),
                "跟拍别人后，原处的人已经在屏外");
            Assert.IsTrue(
                OverheadEmotes.IsVisible(new Vector3(20f, 0f, 0f), followCenter, halfWidth, halfHeight, cameraZ),
                "被跟拍的人始终在画面里");
        }

        [Test]
        public void 冒表情不改时钟背包地块与NPC状态()
        {
            var world = NewWorld();
            FarmStartup.Seed(world);
            var before = Snapshot(world);

            var board = new EmoteBoard();
            for (var i = 0; i < world.Npcs.Count; i++)
            {
                board.Post(world.Npcs[i].Id, AllEvents[i], i);
            }

            Assert.AreEqual(before, Snapshot(world), "表情只是反馈，不该推进任何领域状态");
            Assert.AreEqual(world.Npcs.Count, board.ActiveAt(2f).Count);
        }

        static FarmWorld NewWorld()
        {
            var world = new FarmWorld(new FarmClock());
            foreach (var footprint in FarmLayout.Fields)
            {
                world.AddField(
                    footprint.Id,
                    FarmLayout.ToFarmPosition(footprint.Center),
                    FarmStartup.FieldFertility);
            }

            foreach (var spawn in FarmLayout.NpcSpawns)
            {
                world.AddNpc(
                    spawn.Id,
                    FarmDemoBootstrap.DisplayNameOf(spawn.Id),
                    FarmLayout.ToFarmPosition(spawn.World));
            }

            world.AddSeeds(CropCatalog.RadishId, 12);
            world.AddSeeds(CropCatalog.WheatId, 4);
            return world;
        }

        static string Snapshot(FarmWorld world)
        {
            var builder = new StringBuilder();
            builder.Append(world.Clock.Day).Append('|')
                .Append(world.Clock.DaySeconds.ToString("0.#####")).Append('|')
                .Append(world.Clock.Speed.ToString("0.###"));

            foreach (var field in world.Fields)
            {
                builder.Append("|field:").Append(field.Id).Append(',')
                    .Append(field.State).Append(',')
                    .Append(field.Growth.ToString("0.#####")).Append(',')
                    .Append(field.Water.ToString("0.#####")).Append(',')
                    .Append(field.Fertility.ToString("0.#####")).Append(',')
                    .Append(field.Weeds.ToString("0.#####"));
            }

            foreach (var npc in world.Npcs)
            {
                builder.Append("|npc:").Append(npc.Id).Append(',')
                    .Append(npc.Position.X.ToString("0.#####")).Append(',')
                    .Append(npc.Position.Z.ToString("0.#####")).Append(',')
                    .Append(npc.Stamina.ToString("0.#####")).Append(',')
                    .Append(npc.Mood.ToString("0.#####")).Append(',')
                    .Append(npc.Hunger.ToString("0.#####")).Append(',')
                    .Append(npc.IsResting);
            }

            var ledger = world.Ledger;
            builder.Append("|ledger:")
                .Append(ledger.SeedsAdded).Append(',').Append(ledger.SeedsRemoved).Append(',')
                .Append(ledger.ProduceAdded).Append(',').Append(ledger.ProduceRemoved).Append(',')
                .Append(ledger.HeldAdded).Append(',').Append(ledger.HeldRemoved).Append(',')
                .Append(ledger.SeedTotal).Append(',').Append(ledger.ProduceTotal);
            return builder.ToString();
        }
    }
}
