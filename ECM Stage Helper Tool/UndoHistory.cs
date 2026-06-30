using System;
using System.Collections.Generic;
using System.Linq;

namespace ECM_Stage_Helper_Tool
{
    // -----------------------------------------------------------------------
    // Aktions-Interface (Undo + Redo symmetrisch)
    // -----------------------------------------------------------------------

    internal interface IUndoRedoAction
    {
        void Undo();
        void Redo();
    }

    // -----------------------------------------------------------------------
    // Einzelne Zellwertänderung
    // -----------------------------------------------------------------------

    internal sealed class CellUndoAction : IUndoRedoAction
    {
        private readonly MapModel _map;
        private readonly int _row, _col;
        private readonly double _oldValue;
        private readonly double _newValue;

        public CellUndoAction(MapModel map, int row, int col, double oldValue, double newValue)
        {
            _map      = map;
            _row      = row;
            _col      = col;
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public void Undo() => Apply(_oldValue);
        public void Redo() => Apply(_newValue);

        private void Apply(double val)
        {
            _map.Values[_row, _col] = val;
            if (Math.Abs(_map.GetOriginalValue(_row, _col) - val) < 1e-12)
                _map.UnmarkCell(_row, _col);
            else
                _map.MarkCellModified(_row, _col);
        }
    }

    // -----------------------------------------------------------------------
    // Vollständiger Map-Zustand (für Achsenänderungen / Map-Reset)
    // -----------------------------------------------------------------------

    internal sealed class MapSnapshotUndoAction : IUndoRedoAction
    {
        private readonly MapSnapshot[] _before;
        private MapSnapshot[] _after;         // wird nach der Änderung gesetzt

        public MapSnapshotUndoAction(IEnumerable<MapModel> maps)
        {
            _before = maps.Select(m => new MapSnapshot(m)).ToArray();
        }

        /// <summary>
        /// Muss unmittelbar NACH der eigentlichen Änderung aufgerufen werden,
        /// damit Redo den neuen Zustand kennt.
        /// </summary>
        public void CaptureAfterState()
        {
            _after = _before.Select(s => new MapSnapshot(s.Map)).ToArray();
        }

        public void Undo()
        {
            foreach (var s in _before)
                s.Restore();
        }

        public void Redo()
        {
            if (_after == null) return;
            foreach (var s in _after)
                s.Restore();
        }
    }

    /// <summary>Speichert den vollständigen Zustand einer Map (Achsen + Werte + Markierungen).</summary>
    internal sealed class MapSnapshot
    {
        private readonly double[] _xAxis;
        private readonly double[] _yAxis;
        private readonly double[,] _values;
        private readonly (int row, int col)[] _modifiedCells;

        public MapModel Map { get; }

        public MapSnapshot(MapModel map)
        {
            Map           = map;
            _xAxis        = (double[])map.XAxis.Clone();
            _yAxis        = (double[])map.YAxis.Clone();
            _values       = (double[,])map.Values.Clone();
            _modifiedCells = map.ModifiedCells.ToArray();
        }

        public void Restore()
        {
            Map.XAxis = (double[])_xAxis.Clone();
            Map.YAxis = (double[])_yAxis.Clone();
            Map.Values = (double[,])_values.Clone();
            Map.ModifiedCells.Clear();
            foreach (var c in _modifiedCells)
                Map.ModifiedCells.Add(c);
        }
    }

    // -----------------------------------------------------------------------
    // Undo/Redo-Stack
    // -----------------------------------------------------------------------

    internal sealed class UndoStack
    {
        private readonly Stack<IUndoRedoAction> _undo = new Stack<IUndoRedoAction>();
        private readonly Stack<IUndoRedoAction> _redo = new Stack<IUndoRedoAction>();

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>Neue Aktion: Redo-Stack leeren (Windows-Standard).</summary>
        public void Push(IUndoRedoAction action)
        {
            _undo.Push(action);
            _redo.Clear();
        }

        public bool TryUndo(out IUndoRedoAction action)
        {
            if (_undo.Count == 0) { action = null; return false; }
            action = _undo.Pop();
            _redo.Push(action);
            return true;
        }

        public bool TryRedo(out IUndoRedoAction action)
        {
            if (_redo.Count == 0) { action = null; return false; }
            action = _redo.Pop();
            _undo.Push(action);
            return true;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }
    }
}
