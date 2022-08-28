using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using HarmonyLib;

namespace Exund.FastAPLoad
{
    public class FastAPLoadMod : ModBase
    {
        static BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly Type T_BlockPlacementCollector = typeof(BlockPlacementCollector);
        static readonly FieldInfo m_QueryPositionsSorted = T_BlockPlacementCollector.GetField("m_QueryPositionsSorted", flags);
        static readonly FieldInfo m_BlockToAttach = T_BlockPlacementCollector.GetField("m_BlockToAttach", flags);
        static readonly FieldInfo m_TechToAttachTo = T_BlockPlacementCollector.GetField("m_TechToAttachTo", flags);
        static readonly FieldInfo m_BlockTableCache = T_BlockPlacementCollector.GetField("m_BlockTableCache", flags);

        static readonly Type T_QueryPosition = T_BlockPlacementCollector.GetNestedType("QueryPosition", BindingFlags.NonPublic);
        static readonly ConstructorInfo c_QueryPosition = T_QueryPosition.GetConstructor(new Type[] { typeof(IntVector3) });
        static readonly Type T_List_QueryPosition = typeof(List<>).MakeGenericType(T_QueryPosition);
        static readonly MethodInfo LQP_Add = T_List_QueryPosition.GetMethod("Add");

        internal const string HarmonyID = "Exund.FastAPLoad";
        internal static Harmony harmony = new Harmony(HarmonyID);

        private static bool Inited = false;
        public override void EarlyInit()
        {
            if (!Inited)
            {
                Inited = true;
                Load();
            }
        }

        public override bool HasEarlyInit()
        {
            return true;
        }

        public override void Init()
        {
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public override void DeInit()
        {
            harmony.UnpatchAll(HarmonyID);
        }

#if DEBUG
        public static bool UseModded = true;
        public static long debug_ms;
        public static HashSet<IntVector3> debug_cells = new HashSet<IntVector3>();
        public static Tank debug_tech;
        public static TankBlock debug_block;
        public static string debug_used;
#endif

        public static void Load()
        {
#if DEBUG
            GameObject holder = new GameObject();
            holder.AddComponent<Debug>();

            GameObject.DontDestroyOnLoad(holder);
#endif
        }

        public static void FastAPLoad(BlockPlacementCollector collector, TankBlock m_BlockToAttach, Tank m_TechToAttachTo)
        {
#if DEBUG
            System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
#endif
            var table_cache = (BlockManager.TableCache)m_BlockTableCache.GetValue(collector);
            IntBounds tech_bounds = table_cache.bounds;
            var tech_size = tech_bounds.size;
            var volume = tech_size.x * tech_size.y * tech_size.z;
            Vector3 block_size = m_BlockToAttach.BlockCellBounds.size;
            int maxDim = Mathf.CeilToInt(Mathf.Max(block_size.x, Mathf.Max(block_size.y, block_size.z)));

            if (maxDim + 1 < 6)
            {
                var cells = new HashSet<IntVector3>();
                var bsize = m_BlockToAttach.BlockCellBounds.size;
                maxDim += 1;

                foreach (var bl in m_TechToAttachTo.blockman.IterateBlocks())
                {
                    var rot = bl.transform.localRotation;
                    var pos = bl.transform.localPosition;
                    var size = Vector3.one * (maxDim * 2 - 1);
                    for (int k = 0; k < bl.attachPoints.Length; k++)
                    {
                        if (!bl.ConnectedBlocksByAP[k])
                        {
                            var ap = bl.attachPoints[k];
                            if (ap.x % 1f != 0f)
                            {
                                ap.x += Math.Sign(ap.x) * 0.5f;
                            }
                            if (ap.y % 1f != 0f)
                            {
                                ap.y += Math.Sign(ap.y) * 0.5f;
                            }
                            if (ap.z % 1f != 0f)
                            {
                                ap.z += Math.Sign(ap.z) * 0.5f;
                            }
                            if (maxDim == 1)
                            {
                                cells.Add(rot * ap + pos);
                            }
                            else
                            {
                                var block_bounds = new Bounds(rot * ap + pos, size);
                                IntVector3 intMaxBounds = /*bl.trans.localRotation * */new IntVector3(block_bounds.max);// + bl.trans.localPosition;
                                IntVector3 intMinBounds = /*bl.trans.localRotation * */new IntVector3(block_bounds.min);// + bl.trans.localPosition;

                                for (int x = intMinBounds.x; x <= intMaxBounds.x; x++)
                                {
                                    if (Math.Abs(x) + table_cache.blockTableCentre.x > table_cache.size)
                                        break;
                                    for (int y = intMinBounds.y; y <= intMaxBounds.y; y++)
                                    {
                                        if (Math.Abs(y) + table_cache.blockTableCentre.y > table_cache.size)
                                            break;
                                        for (int z = intMinBounds.z; z <= intMaxBounds.z; z++)
                                        {
                                            if (Math.Abs(z) + table_cache.blockTableCentre.z > table_cache.size)
                                                break;

                                            cells.Add(new IntVector3(x, y, z));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                foreach (var v in cells)
                {
                    LQP_Add.Invoke(m_QueryPositionsSorted.GetValue(collector), new object[] { c_QueryPosition.Invoke(new object[] { v }) });
                }

#if DEBUG
                debug_cells = cells;
#endif
            }
            else
            {
                tech_bounds.min -= IntVector3.one;

                IntBounds intBounds = new IntBounds(tech_bounds.min - IntVector3.one * maxDim, tech_bounds.min + new IntVector3(tech_bounds.size) + IntVector3.one * maxDim);
                for (int k = intBounds.min.x; k <= intBounds.max.x; k++)
                {
                    for (int l = intBounds.min.y; l <= intBounds.max.y; l++)
                    {
                        for (int m = intBounds.min.z; m <= intBounds.max.z; m++)
                        {
                            LQP_Add.Invoke(m_QueryPositionsSorted.GetValue(collector), new object[] { c_QueryPosition.Invoke(new object[] { new IntVector3(k, l, m) }) });
                        }
                    }
                }
            }

#if DEBUG
            stopwatch.Stop();
            debug_ms = stopwatch.ElapsedMilliseconds;

            debug_tech = m_TechToAttachTo;
            debug_block = m_BlockToAttach;
#endif
        }

        static class Patches
        {
            [HarmonyPatch(typeof(BlockPlacementCollector), "CollectPlacements")]
            private static class CollectPlacements
            {
                static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
                {
                    var codes = instructions.ToList();
                    var start_instruction_i = codes.FindIndex(ci => ci.opcode == OpCodes.Br);

                    var label = (Label)codes[start_instruction_i].operand;

                    //var end_instruction_i = codes.FindIndex(start_instruction_i + 1, ci => ci.labels != null && ci.labels.Contains(label));
                    ++start_instruction_i;

                    var ldarg_0 = new CodeInstruction(OpCodes.Ldarg_0);
                    ldarg_0.labels = codes[start_instruction_i].labels;
                    codes[start_instruction_i].labels = new List<Label>();

                    codes.Insert(start_instruction_i, new CodeInstruction(OpCodes.Br_S, label));
                    codes.Insert(start_instruction_i, new CodeInstruction(OpCodes.Call, typeof(FastAPLoadMod).GetMethod("FastAPLoad")));
                    codes.Insert(start_instruction_i, new CodeInstruction(OpCodes.Ldfld, m_TechToAttachTo));
                    codes.Insert(start_instruction_i, new CodeInstruction(OpCodes.Ldarg_0));
                    codes.Insert(start_instruction_i, new CodeInstruction(OpCodes.Ldfld, m_BlockToAttach));
                    codes.Insert(start_instruction_i, new CodeInstruction(OpCodes.Ldarg_0));
                    codes.Insert(start_instruction_i, ldarg_0);
                    /*var brfalse_label = codes[start_instruction_i].labels[0];
                    codes.RemoveRange(start_instruction_i, end_instruction_i - start_instruction_i);
					
					var ldarg_0 = new CodeInstruction(OpCodes.Ldarg_0);
					ldarg_0.labels = new List<Label>() { brfalse_label };

					codes.Insert(start_instruction_i, ldarg_0);*/

                    return codes;
                }
            }

            static Type T_ManPointer = typeof(ManPointer);
            static MethodInfo get_targetPosition = T_ManPointer.GetProperty("targetPosition").GetGetMethod();

            [HarmonyPatch(typeof(ManTechBuilder), "StartStopAttachPointCollection")]
            private static class StartStopAttachPointCollection
            {
                static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
                {
                    var codes = instructions.ToList();
                    var start_instruction_i = codes.FindLastIndex(ci => ci.opcode == OpCodes.Ldsfld) + 1;

                    codes.RemoveAt(start_instruction_i);
                    codes.RemoveAt(start_instruction_i);
                    codes.RemoveAt(start_instruction_i);
                    codes.Insert(start_instruction_i, new CodeInstruction(OpCodes.Callvirt, get_targetPosition));

                    return codes;
                }
            }

            /*[HarmonyPatch(typeof(ManTechBuilder), "UpdateDraggingBlock")]
            private static class UpdateDraggingBlock
            {
                static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
                {
                    var UpdateBlockPosTechLocal = T_BlockPlacementCollector.GetMethod("UpdateBlockPosTechLocal");
                    var codes = instructions.ToList();
                    var start_instruction_i = codes.FindIndex(ci => ci.opcode == OpCodes.Callvirt && ci.operand == UpdateBlockPosTechLocal) - 3;

                    codes.RemoveAt(start_instruction_i);
                    codes.RemoveAt(start_instruction_i);
                    codes.Insert(start_instruction_i, new CodeInstruction(OpCodes.Callvirt, get_targetPosition));
                    codes.Insert(start_instruction_i, new CodeInstruction(OpCodes.Ldsfld, T_ManPointer.GetField("inst", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)));

                    return codes;
                }
            }*/
        }

#if DEBUG
        class Debug : MonoBehaviour
        {
            GameObject cube;

            void Start()
            {
                cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(cube.GetComponent<Collider>());
                var renderer = cube.GetComponent<MeshRenderer>();
                //var shader = Shader.Find("Standard");
                //renderer.material = new Material(shader);
                renderer.material.mainTexture = Texture2D.whiteTexture;
            }

            void Update()
            {
                for (var i = 0; i < gameObject.transform.childCount; ++i)
                {
                    DestroyImmediate(gameObject.transform.GetChild(i).gameObject);
                }

                foreach (var c in debug_cells)
                {
                    var temp = Instantiate(cube, gameObject.transform);
                    temp.transform.localPosition = debug_tech.transform.TransformPoint(c);
                    temp.transform.localRotation = debug_tech.transform.localRotation;
                }
            }

            void OnGUI()
            {

                GUI.BeginGroup(new Rect(100, 0, 300, 500), GUI.skin.box);
                {
                    UseModded = GUILayout.Toggle(UseModded, "Fast AP Load");
                    GUILayout.Label(debug_ms.ToString() + " " + debug_used);
                    if (debug_block)
                    {
                        var bsize = debug_block.BlockCellBounds.size;
                        int maxDim = Mathf.CeilToInt(Mathf.Max(bsize.x, Mathf.Max(bsize.y, bsize.z))) + 1;
                        GUILayout.Label(debug_block.name + " " + bsize.ToString() + " " + maxDim.ToString());
                    }
                }
                GUI.EndGroup();
            }
        }
#endif
    }
}
