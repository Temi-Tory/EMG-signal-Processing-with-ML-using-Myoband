#region Directives
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Text;
using System.Windows.Forms;

using ZedGraph;

using MyoSharp.Communication;
using MyoSharp.Exceptions;
using MyoSharp.Device;
#endregion


namespace Project_Emg_Vis
{

    public partial class Form1 : Form
    {
        #region Constants
        private const int NUMBER_OF_SENSORS = 8;

        private static readonly Color[] DATA_SERIES_COLORS = new Color[]
        {
            Color.Red,
            Color.Blue,
            Color.Green,
            Color.Yellow,
            Color.Pink,
            Color.Orange,
            Color.Purple,
            Color.Black,
        };
        #endregion

        #region Fields
        private readonly DateTime _startTime;
        private readonly PointPairList[] _pointPairs;
        private readonly PaneList Panes;
        private readonly ZedGraphControl _graphControl;
        private readonly IChannel _channel;
        private readonly IHub _hub;
        private readonly List<LineItem> _sortOrderZ;
        #endregion

        public Form1()
        {
            InitializeComponent();

            // we'll calculate all of our incoming data relative to this point in time
            _startTime = DateTime.UtcNow;


            // construct our graph
            _graphControl = new ZedGraphControl() { Dock = DockStyle.Fill };


            _graphControl.MouseClick += GraphControl_MouseClick;
            _graphControl.GraphPane.Title.Text = "Myo EMG Data vs Time";
            MasterPane Main_Pane = _graphControl.MasterPane;
            Main_Pane.PaneList.Clear();

            _pointPairs = new PointPairList[NUMBER_OF_SENSORS];
            _sortOrderZ = new List<LineItem>();
            Panes = new PaneList();

            for (int i = 0; i < 8; i++)
            {
                Panes.Add(new GraphPane());
                Main_Pane.Add(Panes[i]);
                Panes[i].XAxis.Scale.MajorStep = 100;

                Panes[i].YAxis.Scale.Max = 200;
                Panes[i].YAxis.Scale.Min = -200;
                _pointPairs[i] = new PointPairList();

                var dataPointLine = Panes[i].AddCurve("Sensor " + i, _pointPairs[i], DATA_SERIES_COLORS[i]);
                dataPointLine.Line.IsVisible = true;

                _sortOrderZ.Add(dataPointLine);

            }


            Controls.Add(_graphControl);

            // get set up to listen for Myo events
            _channel = Channel.Create(ChannelDriver.Create(ChannelBridge.Create(), MyoErrorHandlerDriver.Create(MyoErrorHandlerBridge.Create())));

            _hub = Hub.Create(_channel);
            _hub.MyoConnected += Hub_MyoConnected;
            _hub.MyoDisconnected += Hub_MyoDisconnected;
        }

        #region Methods

        private void RefreshGraph()
        {
            // force a redraw for new data
            _graphControl.AxisChange();
            _graphControl.Invalidate();
        }

        private void SortZOrderFromClickLocation(ZedGraphControl graphControl, float locationX, float locationY)
        {
            graphControl.GraphPane.FindNearestObject(
                new PointF(locationX, locationY),
                CreateGraphics(),
                out object nearestObject,
                out _);

            if (nearestObject == null)
            {
                return;
            }

            LineItem activeLine = null;
            if (nearestObject.GetType() == typeof(LineItem))
            {
                activeLine = (LineItem)nearestObject;
            }
            else if (nearestObject.GetType() == typeof(Legend))
            {
                var legend = (Legend)nearestObject;

                legend.FindPoint(
                    new PointF(locationX, locationY),
                    graphControl.GraphPane,
                    graphControl.GraphPane.CalcScaleFactor(),
                    out int index);

                if (index >= 0 && index < _sortOrderZ.Count)
                {
                    activeLine = _sortOrderZ[index];
                }
            }

            if (activeLine != null)
            {
                _sortOrderZ.Remove(activeLine);
                _sortOrderZ.Insert(0, activeLine);

                graphControl.GraphPane.CurveList.Sort(new CurveItemComparer(_sortOrderZ));
                graphControl.Invalidate();
            }
        }
        #endregion

        #region MYO Event Handlers
        private void Hub_MyoDisconnected(object sender, MyoEventArgs e)
        {
            e.Myo.EmgDataAcquired -= Myo_EmgDataAcquired;
        }

        private void Hub_MyoConnected(object sender, MyoEventArgs e)
        {
            e.Myo.Unlock(UnlockType.Hold);
            e.Myo.EmgDataAcquired += Myo_EmgDataAcquired;
            e.Myo.SetEmgStreaming(true);

        }

        private void Myo_EmgDataAcquired(object sender, EmgDataEventArgs e)
        {

            // pull data from each sensor
            for (var i = 0; i < _pointPairs.Length; ++i)
            {
                _pointPairs[i].Add((e.Timestamp - _startTime).TotalMilliseconds, e.EmgData.GetDataForSensor(i));

            }
            RefreshGraph();
        }    
        #endregion

        #region Classes
        private class CurveItemComparer : IComparer<CurveItem>
        {
            private readonly IList<CurveItem> _sortOrder;

            public CurveItemComparer(IEnumerable<CurveItem> sortOrder)
            {
                _sortOrder = new List<CurveItem>(sortOrder);
            }

            public int Compare(CurveItem x, CurveItem y)
            {
                return _sortOrder.IndexOf(x).CompareTo(_sortOrder.IndexOf(y));
            }
        }
        #endregion
        #region Form Event Handlers
        private void Form1_Load(object sender, EventArgs e)
        {
            // start listening for Myo data
            _channel.StartListening();


        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _channel.Dispose();
            _hub.Dispose();
        }

        private void GraphControl_MouseClick(object sender, MouseEventArgs e)
        {
            var graphControl = (ZedGraphControl)sender;
            SortZOrderFromClickLocation(graphControl, e.X, e.Y);
        }
        private void TmrRefresh_Tick(object sender, EventArgs e)
        {
            // timer UI component ticks on the UI thread!
            RefreshGraph();
        }
        #endregion

    }
}