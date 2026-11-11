namespace SmartX.Api.Topology;

/// <summary>
/// The result of a range search: the matching values plus how many tree
/// nodes were looked at, so the page can show the search is not scanning
/// everything.
/// </summary>
public sealed record RangeResult<TValue>(List<TValue> Items, int NodesVisited);

/// <summary>
/// An AVL tree (a self-balancing binary search tree). After every insert or
/// remove, each node's left and right sides differ in height by at most 1,
/// so searching stays O(log n) even if the keys arrive in order, for example
/// timestamps (Adelson-Velsky and Landis, 1962; Cormen et al., 2022).
///
/// If two values have the same key (two readings at the same timestamp) they
/// are kept together in a list on the same node, so nothing is overwritten.
/// </summary>
public sealed class AvlTree<TKey, TValue> where TKey : IComparable<TKey>
{
    private sealed class Node(TKey key)
    {
        public TKey Key { get; } = key;
        public List<TValue> Values { get; } = [];
        public Node? Left { get; set; }
        public Node? Right { get; set; }
        public int Height { get; set; } = 1;
    }

    private Node? _root;

    /// <summary>Total number of values stored (duplicates included).</summary>
    public int Count { get; private set; }

    public int Height => HeightOf(_root);

    public void Insert(TKey key, TValue value)
    {
        _root = Insert(_root, key, value);
        Count++;
    }

    private static Node Insert(Node? node, TKey key, TValue value)
    {
        if (node is null)
        {
            var created = new Node(key);
            created.Values.Add(value);
            return created;
        }

        var compare = key.CompareTo(node.Key);
        if (compare < 0)
        {
            node.Left = Insert(node.Left, key, value);
        }
        else if (compare > 0)
        {
            node.Right = Insert(node.Right, key, value);
        }
        else
        {
            // same key: keep both values, the tree shape does not change
            node.Values.Add(value);
            return node;
        }

        return Rebalance(node);
    }

    /// <summary>All values stored under exactly this key.</summary>
    public IReadOnlyList<TValue> Find(TKey key)
    {
        var node = _root;
        while (node is not null)
        {
            var compare = key.CompareTo(node.Key);
            if (compare == 0)
            {
                return node.Values;
            }

            node = compare < 0 ? node.Left : node.Right;
        }

        return [];
    }

    /// <summary>
    /// All values with from &lt;= key &lt;= to, in key order. Whole branches
    /// that cannot hold a match are skipped, which is why this is faster than
    /// checking every item.
    /// </summary>
    public RangeResult<TValue> RangeSearch(TKey from, TKey to)
    {
        var items = new List<TValue>();
        var visited = 0;
        if (from.CompareTo(to) <= 0)
        {
            Collect(_root, from, to, items, ref visited);
        }

        return new RangeResult<TValue>(items, visited);
    }

    private static void Collect(Node? node, TKey from, TKey to, List<TValue> items, ref int visited)
    {
        if (node is null)
        {
            return;
        }

        visited++;

        // only go left if smaller keys could still be in range
        if (from.CompareTo(node.Key) < 0)
        {
            Collect(node.Left, from, to, items, ref visited);
        }

        if (from.CompareTo(node.Key) <= 0 && node.Key.CompareTo(to) <= 0)
        {
            items.AddRange(node.Values);
        }

        // only go right if larger keys could still be in range
        if (to.CompareTo(node.Key) > 0)
        {
            Collect(node.Right, from, to, items, ref visited);
        }
    }

    /// <summary>Every value in key order (used by the tests).</summary>
    public List<TValue> ToList()
    {
        var items = new List<TValue>();
        InOrder(_root, items);
        return items;
    }

    private static void InOrder(Node? node, List<TValue> items)
    {
        if (node is null)
        {
            return;
        }

        InOrder(node.Left, items);
        items.AddRange(node.Values);
        InOrder(node.Right, items);
    }

    /// <summary>
    /// Removes the smallest key (the oldest reading) and everything stored
    /// under it. Used to stop the history growing forever.
    /// </summary>
    public bool RemoveSmallest()
    {
        if (_root is null)
        {
            return false;
        }

        _root = RemoveSmallest(_root, out var removed);
        Count -= removed;
        return true;
    }

    private static Node? RemoveSmallest(Node node, out int removed)
    {
        if (node.Left is null)
        {
            removed = node.Values.Count;
            return node.Right;
        }

        node.Left = RemoveSmallest(node.Left, out removed);
        return Rebalance(node);
    }

    /// <summary>True if every node is balanced and its stored height is correct (used by the tests).</summary>
    public bool IsBalanced() => CheckBalanced(_root);

    private static bool CheckBalanced(Node? node)
    {
        if (node is null)
        {
            return true;
        }

        var left = HeightOf(node.Left);
        var right = HeightOf(node.Right);
        return Math.Abs(left - right) <= 1
            && node.Height == 1 + Math.Max(left, right)
            && CheckBalanced(node.Left)
            && CheckBalanced(node.Right);
    }

    // ---- balancing ----

    private static int HeightOf(Node? node) => node?.Height ?? 0;

    private static int BalanceOf(Node node) => HeightOf(node.Left) - HeightOf(node.Right);

    private static void UpdateHeight(Node node) =>
        node.Height = 1 + Math.Max(HeightOf(node.Left), HeightOf(node.Right));

    private static Node Rebalance(Node node)
    {
        UpdateHeight(node);
        var balance = BalanceOf(node);

        if (balance > 1)
        {
            // left side too tall; if the left child leans right, fix that first
            if (BalanceOf(node.Left!) < 0)
            {
                node.Left = RotateLeft(node.Left!);
            }

            return RotateRight(node);
        }

        if (balance < -1)
        {
            // right side too tall; if the right child leans left, fix that first
            if (BalanceOf(node.Right!) > 0)
            {
                node.Right = RotateRight(node.Right!);
            }

            return RotateLeft(node);
        }

        return node;
    }

    private static Node RotateRight(Node node)
    {
        var newTop = node.Left!;
        node.Left = newTop.Right;
        newTop.Right = node;
        UpdateHeight(node);
        UpdateHeight(newTop);
        return newTop;
    }

    private static Node RotateLeft(Node node)
    {
        var newTop = node.Right!;
        node.Right = newTop.Left;
        newTop.Left = node;
        UpdateHeight(node);
        UpdateHeight(newTop);
        return newTop;
    }
}
