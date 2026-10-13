using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// The tech tree of the workbench in reach (G7, GROUND.md §6.8), in RustMVP's manner: the tree laid out from its roots
// down, each node the item's slot with its salvage under it, lines from the nodes before it (green once known); the
// chosen node's details on the right with UNLOCK. Known nodes are ticked, locked ones dim and drawn in blueprint blue.
public partial class GroundUi
{
    private static readonly Color Blueprint = new(0.36f, 0.55f, 0.80f);
    private string? _techNode;

    /// <summary>Opens the tech tree of the workbench in reach (the game, when the player uses a workbench).</summary>
    public void OpenTechTree() => Open(GroundScreen.TechTree);

    /// <summary>Chooses a node on the tech tree (for screenshots).</summary>
    public void SelectTechNode(string key) => _techNode = key;

    private void PaintTechTree()
    {
        TechTreeView? tree = Model.TechTree;
        _o = TopFrame;
        Button("", 802, 0, 250, 60, () => Open(GroundScreen.Inventory), 30);
        Arrow(833, 33, -1);
        Text("INVENTORY", 869, 13, 30, G.Ink, bold: true, condensed: true);

        _o = CanvasFrame;
        Rect(87, 65, 1046, 690, G.Panel);
        PanelArea(79, 65, 1414, 690);
        if (tree == null || tree.Nodes.Count == 0)
        {
            Text("NO WORKBENCH IN REACH", 110, 90, 40, G.Ink, bold: true, condensed: true);
            Paragraph("Stand at a workbench to see its tech tree: what it can teach you, and for how much salvage.", 112, 140, 900, 21, G.Ink);
            return;
        }
        Text($"WORKBENCH LEVEL {tree.Workbench}", 110, 78, 36, G.Ink, bold: true, condensed: true);
        Text("TECH TREE", 112, 118, 18, G.Muted, bold: true);
        Icon("salvage", 1010, 78, 36, 36);
        TextIn(tree.Salvage.ToString("N0", CultureInfo.InvariantCulture), 900, 84, 104, 24, G.Ink, HorizontalAlignment.Right, bold: true);

        Dictionary<string, (float X, float Y)> places = LayOut(tree, 110, 160, 1000, 580);
        var byKey = new Dictionary<string, TechNodeView>();
        foreach (TechNodeView node in tree.Nodes)
        {
            byKey[node.Key] = node;
        }
        const float size = 76;
        // Lines first: from each node before to the node, down then across then down.
        foreach (TechNodeView node in tree.Nodes)
        {
            (float x, float y) = places[node.Key];
            foreach (string before in node.After)
            {
                if (!places.TryGetValue(before, out (float X, float Y) from))
                {
                    continue;
                }
                bool known = byKey[before].State == TechNodeState.Known;
                Color colour = known ? new Color(G.Green, 0.8f) : new Color(G.Ink, 0.25f);
                float mid = (from.Y + size + y) / 2;
                Line(colour, 3, new Vector2(from.X + size / 2, from.Y + size + 18), new Vector2(from.X + size / 2, mid + 4),
                    new Vector2(x + size / 2, mid + 4), new Vector2(x + size / 2, y));
            }
        }
        TechNodeView? chosen = null;
        foreach (TechNodeView node in tree.Nodes)
        {
            (float x, float y) = places[node.Key];
            string key = node.Key;
            bool selected = _techNode == key;
            if (selected)
            {
                chosen = node;
            }
            Color fill = node.State switch
            {
                TechNodeState.Known => new Color(0.31f, 0.44f, 0.18f, 0.72f),
                TechNodeState.Open => G.PanelHover,
                _ => new Color(0.10f, 0.11f, 0.12f, 0.55f),
            };
            Button("", x, y, size, size, () => _techNode = key, fill: selected ? G.Selected : fill, tip: key);
            if (node.State == TechNodeState.Locked)
            {
                Rect(x + 4, y + 4, size - 8, size - 8, new Color(Blueprint, 0.28f));
            }
            Icon(key, x + 6, y + 6, size - 12, size - 12, node.State == TechNodeState.Locked ? 0.45f : 1f);
            if (node.State == TechNodeState.Known)
            {
                Glyph("check", x + size - 24, y + 4, 20, G.Ink);
            }
            else
            {
                // The price on a dark strip under the node, so it reads over any backdrop.
                bool afford = tree.Salvage >= node.Cost;
                Rect(x, y + size + 2, size, 20, new Color(0.10f, 0.11f, 0.12f, 0.6f));
                Icon("salvage", x + 8, y + size + 4, 16, 16);
                TextIn(node.Cost.ToString(CultureInfo.InvariantCulture), x + 26, y + size + 1, size - 32, 16,
                    node.State == TechNodeState.Open && afford ? G.Ink : new Color(G.Ink, 0.5f), HorizontalAlignment.Left, bold: true);
            }
        }
        PaintTechNode(chosen ?? FirstOpen(tree), tree);
    }

    private static TechNodeView? FirstOpen(TechTreeView tree)
    {
        foreach (TechNodeView node in tree.Nodes)
        {
            if (node.State == TechNodeState.Open)
            {
                return node;
            }
        }
        return tree.Nodes.Count > 0 ? tree.Nodes[0] : null;
    }

    // The chosen node: what it is, what it costs, and UNLOCK (or why not).
    private void PaintTechNode(TechNodeView? node, TechTreeView tree)
    {
        Rect(1137, 65, 356, 690, G.Panel);
        if (node == null)
        {
            return;
        }
        _techNode ??= node.Key;
        ItemInfo info = Model.Item(node.Key);
        Rect(1145, 73, 340, 220, new Color(0.10f, 0.11f, 0.12f, 0.35f));
        Icon(node.Key, 1235, 83, 160, 160, node.State == TechNodeState.Locked ? 0.55f : 1f);
        TextIn(info.Name.ToUpperInvariant(), 1145, 248, 340, 26, G.Ink, HorizontalAlignment.Center, bold: true);
        Paragraph(info.Description, 1153, 304, 324, 18, G.Ink, 7);
        string? before = null;
        foreach (string key in node.After)
        {
            before = before == null ? Model.Item(key).Name : before + " or " + Model.Item(key).Name;
        }
        string status = node.State switch
        {
            TechNodeState.Known => "YOU KNOW THIS BLUEPRINT",
            TechNodeState.Locked => $"UNLOCK {before?.ToUpperInvariant()} FIRST",
            _ when tree.Salvage < node.Cost => $"YOU NEED {node.Cost - tree.Salvage} MORE SALVAGE",
            _ => "READY TO UNLOCK",
        };
        Rect(1145, 600, 340, 30, node.State == TechNodeState.Known ? new Color(0.31f, 0.44f, 0.18f, 0.72f)
            : node.State == TechNodeState.Open && tree.Salvage >= node.Cost ? new Color(G.Selected, 0.5f) : new Color(G.Red, 0.45f));
        TextIn(status, 1145, 605, 340, 16, G.Ink, HorizontalAlignment.Center, bold: true);
        bool can = node.State == TechNodeState.Open && tree.Salvage >= node.Cost;
        string key2 = node.Key;
        Button(node.State == TechNodeState.Known ? "KNOWN" : $"UNLOCK  ·  {node.Cost} SALVAGE", 1145, 640, 340, 56, can ? () => Model.Unlock(key2) : null, 24,
            can ? new Color(G.Green, 0.6f) : G.Panel, enabled: can);
        Paragraph("Unlocked blueprints last through death. A research table teaches what a tree doesn't.", 1153, 704, 324, 15, new Color(G.Ink, 0.8f), 3);
    }

    // Each subtree takes as many columns as it has leaves, its root centred over them; rows by depth.
    private static Dictionary<string, (float X, float Y)> LayOut(TechTreeView tree, float left, float top, float width, float height)
    {
        var children = new Dictionary<string, List<TechNodeView>>();
        var roots = new List<TechNodeView>();
        int depth = 0;
        foreach (TechNodeView node in tree.Nodes)
        {
            depth = Math.Max(depth, node.Depth);
            if (node.After.Count == 0)
            {
                roots.Add(node);
                continue;
            }
            string parent = node.After[0];
            if (!children.TryGetValue(parent, out List<TechNodeView>? list))
            {
                list = new List<TechNodeView>();
                children[parent] = list;
            }
            list.Add(node);
        }
        var columns = new Dictionary<string, float>();
        float next = 0;
        float Place(TechNodeView node)
        {
            if (!children.TryGetValue(node.Key, out List<TechNodeView>? kids) || kids.Count == 0)
            {
                columns[node.Key] = next;
                return next++;
            }
            float first = float.MaxValue, last = float.MinValue;
            foreach (TechNodeView kid in kids)
            {
                float c = Place(kid);
                first = Math.Min(first, c);
                last = Math.Max(last, c);
            }
            return columns[node.Key] = (first + last) / 2;
        }
        foreach (TechNodeView root in roots)
        {
            Place(root);
        }
        float step = Math.Min(118, width / Math.Max(1, next));
        float rowStep = Math.Min(150, height / Math.Max(1, depth + 1));
        float start = left + (width - step * next) / 2;
        var places = new Dictionary<string, (float, float)>();
        foreach (TechNodeView node in tree.Nodes)
        {
            float column = columns.GetValueOrDefault(node.Key, 0);
            places[node.Key] = (MathF.Round(start + column * step + (step - 76) / 2), MathF.Round(top + node.Depth * rowStep));
        }
        return places;
    }
}
