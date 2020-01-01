using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using System.Globalization;
using System.Web.Script.Serialization;
using System.Threading.Tasks;
using LitePlacer;
using System.Linq;
using System.Collections.Generic;
using System.Text;
namespace LitePlacer
{

    public class CNC
    {
        static FormMain MainForm;

        public enum ControlBoardType { TinyG, qQuintic, other, unknown};
        public ControlBoardType Controlboard = ControlBoardType.unknown;
        public WebSocket websocket1;
        static ManualResetEventSlim _readyEvent = new ManualResetEventSlim(false);
        public System.Windows.Forms.Timer statusTimer;

        public CNC(FormMain MainF,WebSocket _websocket1)
        {
            MainForm = MainF;
            websocket1 = _websocket1;
            SlowXY = false;
            SlowZ = false;
            SlowA = false;
        }

        public ManualResetEventSlim ReadyEvent
        {
            
            get
            {
                return _readyEvent;
            }
        }

        public bool Connected { get; set; }
        public bool ErrorState { get; set; }

        public void Error()
        {
            ErrorState = true;
            // Connected = false;
            Homing = false;
            _readyEvent.Set();
            MainForm.UpdateCncConnectionStatus();
        }

        public void Close()
        {
            websocket1.WebSocketClose();
            ErrorState = false;
            Connected = false;
            Homing = false;
            _readyEvent.Set();
            MainForm.UpdateCncConnectionStatus();
            if (statusTimer != null)
            {
                statusTimer.Enabled = false;
                statusTimer.Stop();
            }
        }

        // =================================================================================
        public bool Connect(String name)
        {
            _readyEvent.Set();
            if (websocket1.IsOpen)
            {
                //MainForm.DisplayText("Already connected to serial port " + name + ": already open....Closing");
                return true;
            }
            bool result = websocket1.Connect(MainForm.websocketiptextBox.Text);
            
            ErrorState = false;
            Homing = false;
            Connected = websocket1.IsOpen;
            if (!result)
            {
               // MainForm.DisplayText("Connecting to websocket failed.");
                statusTimer.Stop();
                Error();
            }
            else
            {
                //MainForm.DisplayText("Connected to websocket " + name);
                statusTimer = new System.Windows.Forms.Timer();
                statusTimer.Tick += new System.EventHandler(this.statusTimer_tick);
                statusTimer.Interval = 2000;
                statusTimer.Enabled = true;
                statusTimer.Start();
            }

            return Connected;
        }


        public bool Write(string command)
        {
            if (!websocket1.IsOpen)
            {
                //MainForm.DisplayText("###" + command + " discarded, Websocket not open (readyevent set)");
                _readyEvent.Set();
                Connected = false;
                return false;
            }
            if (ErrorState)
            {
                //MainForm.DisplayText("###" + command + " discarded, error state on (readyevent set)");
                _readyEvent.Set();
                return false;
            }
            _readyEvent.Reset();
            websocket1.WebSocketSend(command);
           _readyEvent.Wait();
            return true;
        }

        public void statusTimer_tick(object timer, EventArgs e)
        {
            if (websocket1.IsOpen)
                websocket1.WebSocketSend("?");
            statusTimer.Enabled = true;
        }

        public bool RawWrite(string command)
        {
            if (!websocket1.IsOpen)
            {
                //MainForm.DisplayText("###" + command + " discarded, websocket not open");
                Connected = false;
                return false;
            }
            if (ErrorState)
            {
               // MainForm.DisplayText("###" + command + " discarded, error state on");
                return false;
            }
            bool res = websocket1.WebSocketSend(command);
            if (!res)
            {
                Error();
            }
            return res;
        }

        public void ForceWrite(string command)
        {
            websocket1.WebSocketSend(command);
        }

        // =================================================================================
        // Square compensation:
        // The machine will be only approximately square. Fortunately, the squareness is easy to measure with camera.
        // User measures correction value, that we apply to movements and reads.
        // For example, correction value is +0.002, meaning that for every unit of +Y movement, 
        // the machine actually also unintentionally moves 0.002 units to +X. 
        // Therefore, for each movement when the user wants to go to (X, Y),
        // we really go to (X - 0.002*Y, Y)

        // CurrentX/Y is the corrected value that user sees and uses, and reflects a square machine
        // TrueX/Y is what the TinyG actually uses.

        public static double SquareCorrection { get; set; }

		private static double CurrX;
		private static double _trueX;

        public double TrueX
        {
            get
            {
                return (_trueX);
            }
            set
            {
                _trueX = value;
            }
        }

		public double CurrentX
        {
            get
            {
                return (CurrX);
            }
            set
            {
                CurrX = value;
            }
        }

        public static void setCurrX(double x)
        {
			_trueX = x;
			CurrX = x - CurrY * SquareCorrection;
            //MainForm.DisplayText("CNC.setCurrX: x= " + x.ToString() + ", CurrX= " + CurrX.ToString() + ", CurrY= " + CurrY.ToString());
        }

        private static double CurrY;
        public double CurrentY
        {
            get
            {
                return (CurrY);
            }
            set
            {
                CurrY = value;
			}
        }
        public static void setCurrY(double y)
        {
            CurrY = y;
			CurrX = _trueX - CurrY * SquareCorrection;
			//MainForm.DisplayText("CNC.setCurrY: "+ y.ToString()+ " CurrX= " + CurrX.ToString());
		}

        private static double CurrZ;
        public double CurrentZ
        {
            get
            {
                return (CurrZ);
            }
            set
            {
                CurrZ = value;
            }
        }
        public static void setCurrZ(double z)
        {
            CurrZ = z;
        }

        private static double CurrA;
        public double CurrentA
        {
            get
            {
                return (CurrA);
            }
            set
            {
                CurrA = value;
            }
        }
        public static void setCurrA(double a)
        {
            CurrA = a;
        }

        public bool SlackCompensation { get; set; }
        private double SlackCompensationDistance = 0.4;

        public bool SlackCompensationA { get; set; }
        private double SlackCompensationDistanceA = 5.0;

        public string SmallMovementString = "G1F200 ";

        public bool SlowXY { get; set; }
        public double SlowSpeedXY { get; set; }

        public bool SlowZ { get; set; }
        public double SlowSpeedZ { get; set; }

        public bool SlowA { get; set; }
        public double SlowSpeedA { get; set; }


        public void XY(double X, double Y)
        {
            double dX = Math.Abs(X - CurrentX);
            double dY = Math.Abs(Y - CurrentY);
            if ((dX < 0.004) && (dY < 0.004))
            {
                //MainForm.DisplayText(" -- zero XY movement command --", KnownColor.Gray);
               // MainForm.DisplayText("ReadyEvent: zero movement command", KnownColor.Gray);
                _readyEvent.Set();
                return;   // already there
            }
            if ((!SlackCompensation)
                ||
                ((CurrentX < X) && (CurrentY < Y))
                )
            {
                XY_move(X, Y);
            }
            else
            {
                XY_move(X - SlackCompensationDistance, Y - SlackCompensationDistance);
                XY_move(X, Y);
            }

        }

        private void XY_move(double X, double Y)
        {
            string command;
            double dX = Math.Abs(X - CurrentX);
            double dY = Math.Abs(Y - CurrentY);
            if ((dX < 0.004) && (dY < 0.004))
            {
                //MainForm.DisplayText(" -- zero XY movement command --", KnownColor.Gray);
                //MainForm.DisplayText("ReadyEvent: zero movement command", KnownColor.Gray);
                _readyEvent.Set();
                return;   // already there
            }
			X = X + SquareCorrection * Y;
			X = Math.Round(X, 3);
            if ((dX < 1) && (dY < 1))
            {
                // Small move
                if (SlowXY)
                {
                    if ((double)MainForm.Setting.CNC_SmallMovementSpeed > SlowSpeedXY)
                    {
                        command = SmallMovementString + "X" + X.ToString(CultureInfo.InvariantCulture) +
                                                       " Y" + Y.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        command = "G1F" + SlowSpeedXY.ToString()
                                + "X" + X.ToString(CultureInfo.InvariantCulture) + "Y" + Y.ToString(CultureInfo.InvariantCulture);
                    }
                }
                else
                {
                    command = SmallMovementString + "X" + X.ToString(CultureInfo.InvariantCulture) +
                                                   " Y" + Y.ToString(CultureInfo.InvariantCulture);
                }
            }
            else
            {
                // large move
                if (SlowXY)
                {
                    command = "G1F" + SlowSpeedXY.ToString()
                            + "X" + X.ToString(CultureInfo.InvariantCulture) + "Y" + Y.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    command = "G0" + "X" + X.ToString(CultureInfo.InvariantCulture) + "Y" + Y.ToString(CultureInfo.InvariantCulture);
                }
            }
            _readyEvent.Reset();
            MainForm.DisplayText("XY Move command = " + command);
            websocket1.WebSocketSend(command);
            _readyEvent.Wait();
            CurrentX =  X;
            CurrentY =  Y;
            MainForm.Update_xpos(CurrentX.ToString());
            MainForm.Update_ypos(CurrentY.ToString());
        }

        public void XYA(double X, double Y, double Am)
        {
            bool CompensateXY = false;
            bool CompensateA = false;

            if ((SlackCompensation) && ((CurrentX > X) || (CurrentY > Y)))
            {
                CompensateXY = true;
            }

            if ((SlackCompensationA) && (CurrentA > (Am - SlackCompensationDistanceA)))
            {
                CompensateA = true;
            }


            if ((!CompensateXY) && (!CompensateA))
            {
                XYA_move(X, Y, Am);
            }
            else if ((CompensateXY) && (!CompensateA))
            {
                XYA_move(X - SlackCompensationDistance, Y - SlackCompensationDistance, Am);
                XY_move(X, Y);
            }
            else if ((!CompensateXY) && (CompensateA))
            {
                XYA_move(X, Y, Am-SlackCompensationDistanceA);
                A_move(Am);
            }
            else
            {
                XYA_move(X - SlackCompensationDistance, Y - SlackCompensationDistance, Am - SlackCompensationDistanceA);
                XYA_move(X, Y, Am);
            }
        }

        private void XYA_move(double X, double Y, double Am)
        {
            string command;
            double dX = Math.Abs(X - CurrentX);
            double dY = Math.Abs(Y - CurrentY);
            double dA = Math.Abs(Am - CurrentA);
            if ((dX < 0.004) && (dY < 0.004) && (dA < 0.01))
            {
               // MainForm.DisplayText(" -- zero XYA movement command --", KnownColor.Gray);
                //MainForm.DisplayText("ReadyEvent: zero movement command", KnownColor.Gray);
                _readyEvent.Set();
                return;   // already there
            }

            X = X + SquareCorrection * Y;
            if ((dX < 1.0) && (dY < 1.0))
            {
                // small movement
                // First do XY move, then A. This works always.
                // (small moves and fast settings can sometimes cause problems)
                if ((dX < 0.004) && (dY < 0.004) )
                {
                    //MainForm.DisplayText(" -- XYA command, XY already there --", KnownColor.Gray);
                }
                else
                {
                    if (SlowXY)
                    { 
                        if ((double)Properties.Settings.Default.CNC_SmallMovementSpeed > SlowSpeedXY)
                        {
                            command = SmallMovementString + "X" + X.ToString(CultureInfo.InvariantCulture) +
                                                           "Y" + Y.ToString(CultureInfo.InvariantCulture);
                        }
                        else
                        {
                                command = "G1F" + SlowSpeedXY.ToString()
                                        + "X" + X.ToString(CultureInfo.InvariantCulture) + "Y" + Y.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                    else
                    {
                        command = SmallMovementString + "X" + X.ToString(CultureInfo.InvariantCulture) +
                                                       "Y" + Y.ToString(CultureInfo.InvariantCulture);
                    }
                    _readyEvent.Reset();
                    websocket1.WebSocketSend(command);
                    _readyEvent.Wait();
                }

                // then A:
                if (dA < 0.01)
                {
                    //MainForm.DisplayText(" -- XYA command, XY already there --", KnownColor.Gray);
                }
                else
                {
                    if (SlowA)
                    {
                        command = "G1F" + SlowSpeedA.ToString() + "A" + Am.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        command = "G0A" + Am.ToString(CultureInfo.InvariantCulture);
                    }
                    _readyEvent.Reset();
                    websocket1.WebSocketSend(command);
                    _readyEvent.Wait();
                }
            }
            else
            {
                // normal case, large move
                // Possibilities:
                // SlowA, SlowXY
                // SlowA, FastXY
                // FastA, SlowXY
                // Fast A, Fast XY
                // To avoid side effects, we'll separate a and xy for first three cases
                if (SlowA || (!SlowA && SlowXY))
                {
                    // Do A first, then XY
                    if (dA < 0.01)
                    {
                        //MainForm.DisplayText(" -- XYA command, XY already there --", KnownColor.Gray);
                    }
                    else
                    {
                        if (SlowA)
                        {
                            command = "G1F" + SlowSpeedA.ToString() + "A" + Am.ToString(CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            command = "G0A" + Am.ToString(CultureInfo.InvariantCulture);
                        }
                        _readyEvent.Reset();
                        websocket1.WebSocketSend(command);
                        _readyEvent.Wait();
                    }
                    // A done, we know XY is slow and large
                    command = "G1F" + SlowSpeedXY.ToString() +
                                "X" + X.ToString(CultureInfo.InvariantCulture) +
                                "Y" + Y.ToString(CultureInfo.InvariantCulture);
                    _readyEvent.Reset();
                    websocket1.WebSocketSend(command);
                    _readyEvent.Wait();
                }
                else
                {
                    // Fast A, Fast XY
                    command = "G0" + "X" + X.ToString(CultureInfo.InvariantCulture) +
                                     "Y" + Y.ToString(CultureInfo.InvariantCulture) +
                                     "A" + Am.ToString(CultureInfo.InvariantCulture);
                     _readyEvent.Reset();
                    MainForm.DisplayText("XYA Move command = " + command);
                    websocket1.WebSocketSend(command);
                    _readyEvent.Wait();
               }
            }
            
            CurrentX =  X;
            CurrentY =  Y;
            CurrentA =  Am;
            MainForm.Update_xpos(CurrentX.ToString());
            MainForm.Update_ypos(CurrentY.ToString());
            MainForm.Update_apos(CurrentA.ToString());

        }


        public void Z(double Z)
        {
            string command = "G0 Z" + Z.ToString(CultureInfo.InvariantCulture);
            double dZ = Math.Abs(Z - CurrentZ);
            if (dZ < 0.005)
            {
                //MainForm.DisplayText(" -- zero Z movement command --");
                //MainForm.DisplayText("ReadyEvent: zero movement command");
                _readyEvent.Set();
                return;   // already there
            }
            if (dZ < 1.1)
            {
                if (SlowZ)
                {
                    if ((double)MainForm.Setting.CNC_SmallMovementSpeed > SlowSpeedZ)
                    {
                        command = "G1F" + SlowSpeedZ.ToString() + "Z" + Z.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        command = "G1F" + MainForm.Setting.CNC_SmallMovementSpeed.ToString() + "Z" + Z.ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
            else
            {
                if (SlowZ)
                {
                    command = "G1F" + SlowSpeedZ.ToString() + "Z" + Z.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    command = "G0Z" + Z.ToString(CultureInfo.InvariantCulture);
                }
            }
            MainForm.DisplayText("Move Z command = " + command);
            _readyEvent.Reset();
            websocket1.WebSocketSend(command);
            _readyEvent.Wait();
            CurrentZ = Z;
            MainForm.Update_zpos(CurrentZ.ToString());
        }

        public void A(double A)
        {
            if (Math.Abs(A - CurrentA) < 0.01)
            {
                //MainForm.DisplayText(" -- zero A movement command --");
                _readyEvent.Set();
                return;   // already there
            }
            if ((SlackCompensationA) && (CurrentA > (A - SlackCompensationDistanceA)))
            {
                A_move(A - SlackCompensationDistanceA);
                A_move(A);
            }
            else
            {
                A_move(A);
            }
        }
        private void A_move(double A)
        {
            string command;
            if (SlowA)
            {
                command = "G1F" + SlowSpeedA.ToString() + " A" + A.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                command = "G0A" + A.ToString(CultureInfo.InvariantCulture);
            }
            _readyEvent.Reset();
            websocket1.WebSocketSend(command);
            _readyEvent.Wait();
            CurrentA = A;
            MainForm.Update_apos(CurrentA.ToString());
        }

        // =================================================================================
        public bool Homing { get; set; }
        public bool IgnoreError { get; set; }

        // =================================================================================
        public void ProbingMode(bool set)
        {
            int wait = 250;
            double b = MainForm.Setting.General_ZprobingHysteresis;
            string backoff = b.ToString("0.00", CultureInfo.InvariantCulture);

            if (Controlboard==ControlBoardType.TinyG)
            {
                //if (set)
                //{
                //    MainForm.DisplayText("Probing mode on, TinyG");
                //    MainForm.CNC_Write_m("{\"zsn\",0}");
                //    Thread.Sleep(wait);
                //    MainForm.CNC_Write_m("{\"zsx\",1}");
                //    Thread.Sleep(wait);
                //    MainForm.CNC_Write_m("{\"zzb\"," + backoff + "}");
                //    Thread.Sleep(wait);
                //}
                //else
                //{
                //    MainForm.DisplayText("Probing mode off, TinyG");
                //    MainForm.CNC_Write_m("{\"zsn\",3}");
                //    Thread.Sleep(wait);
                //    MainForm.CNC_Write_m("{\"zsx\",2}");
                //    Thread.Sleep(wait);
                //    MainForm.CNC_Write_m("{\"zzb\",2}");
                //    Thread.Sleep(wait);
                //}
            }
            else if (Controlboard == ControlBoardType.qQuintic)
            {
                //MainForm.DisplayText("Set probing mode, qQuintic -- SKIPPED --");
                //if (set)
                //{
                //    MainForm.DisplayText("Probing mode on, qQuintic");
                //}
                //else
                //{
                //    MainForm.DisplayText("Probing mode off, qQuintic");
                //}
            }
            else
            {
                //MainForm.DisplayText("Set probing mode, unknown board!!", KnownColor.DarkRed, true);
            }
        }

        // =================================================================================
        public void MotorPowerOn()
        {
            if (Controlboard == ControlBoardType.TinyG)
            {
                MainForm.DisplayText("MotorPowerOff(), TinyG");
                MainForm.CNC_Write_m("me");
            }
            else if (Controlboard == ControlBoardType.qQuintic)
            {
                MainForm.DisplayText("MotorPowerOff(), qQuintic  -- SKIPPED --");
            }
            else
            {
                MainForm.DisplayText("*** MotorPowerOff(), unknown board!!", KnownColor.DarkRed, true);
            }
        }

        public void MotorPowerOff()
        {
            if (Controlboard == ControlBoardType.TinyG)
            {
                //MainForm.DisplayText("MotorPowerOff(), TinyG");
                //MainForm.CNC_Write_m("md");
            }
            else if (Controlboard == ControlBoardType.qQuintic)
            {
                MainForm.DisplayText("MotorPowerOff(), qQuintic  -- SKIPPED --");
            }
            else
            {
                MainForm.DisplayText("*** MotorPowerOff(), unknown board!!", KnownColor.DarkRed, true);
            }
        }

        // =================================================================================
        private bool VacuumIsOn = false;

        public void VacuumDefaultSetting()
        {
            //VacuumIsOn = true;      // force action
            VacuumOff();
        }

        public void VacuumOn()
        {
            string command = "M08";
            if (MainForm.Setting.General_VacuumOutputInverted)
            {
                command = "M09";
            }

            MainForm.DisplayText("VacuumOn()");
                if (!VacuumIsOn)
                {
                    if (RawWrite(command))
                    {
                        VacuumIsOn = true;
                        Thread.Sleep(MainForm.Setting.General_PickupVacuumTime);
                    }
                }
            MainForm.Vacuum_checkBox.Checked = VacuumIsOn;
            Thread.Sleep(300);
        }

        public void VacuumOff()
        {

            string command = "M09";
            if (MainForm.Setting.General_VacuumOutputInverted)
            {
                command = "M08";
            }
            
            MainForm.DisplayText("VacuumOff()");
            if (VacuumIsOn)
                {
                    if (RawWrite(command))
                    {
                        VacuumIsOn = false;
                        Thread.Sleep(MainForm.Setting.General_PickupReleaseTime);
                    }
                }
            MainForm.Vacuum_checkBox.Checked = VacuumIsOn;
            Thread.Sleep(300);
        }

        // =================================================================================
        public bool PumpIsOn = false;

        public void PumpDefaultSetting()
        {
            //PumpIsOn = true;   // to force action
            //PumpOff();
        }

        private void BugWorkaround()
        {
            // see https://www.synthetos.com/topics/file-not-open-error/#post-7194
            // Summary: In some cases, we need a dummy move.
            MainForm.CNC_Z_m(CurrentZ - 0.01);
            MainForm.CNC_Z_m(CurrentZ + 0.01);
        }

        //public void PumpOn()
        //{
        //    string command = "M03";
        //    if (MainForm.Setting.General_PumpOutputInverted)
        //    {
        //        command = "M05";
        //    }
        //    if (Controlboard == ControlBoardType.TinyG)
        //    {
        //        MainForm.DisplayText("PumpOn(), TinyG");
        //        if (!PumpIsOn)
        //        {
        //                if (RawWrite(command))
        //            {
        //                BugWorkaround();
        //                Thread.Sleep(500);  // this much to develop vacuum
        //                PumpIsOn = true;
        //            }
        //        }
        //    }
        //    else if (Controlboard == ControlBoardType.qQuintic)
        //    {
        //        MainForm.DisplayText("PumpOn(), qQuintic  -- SKIPPED --");
        //    }
        //    else
        //    {
        //        MainForm.DisplayText("PumpOn(), TinyG");
        //    }
        //    MainForm.Pump_checkBox.Checked = PumpIsOn;
        //}

        //public void PumpOff()
        //{
        //    string command = "M05";
        //    if (MainForm.Setting.General_PumpOutputInverted)
        //    {
        //        command = "M03";
        //    }
        //    if (Controlboard == ControlBoardType.TinyG)
        //    {
        //        MainForm.DisplayText("PumpOff(), TinyG");
        //        if (PumpIsOn)
        //        {
        //            if (RawWrite(command))
        //            {
        //                Thread.Sleep(50);
        //                BugWorkaround();
        //                PumpIsOn = false;
        //            }
        //        }
        //    }
        //    else if (Controlboard == ControlBoardType.qQuintic)
        //    {
        //        MainForm.DisplayText("PumpOff(), qQuintic  -- SKIPPED --");
        //    }
        //    else
        //    {
        //        MainForm.DisplayText("PumpOff(), qQuintic  -- SKIPPED --");
        //    }
        //    MainForm.Pump_checkBox.Checked = PumpIsOn;
        //}

        public void PumpOff_NoWorkaround()
        // For error situations where we don't want to do the dance
        {
            string command = "M05";
            if (MainForm.Setting.General_PumpOutputInverted)
            {
                command = "M03";
            }
            //MainForm.DisplayText("PumpOff_NoWorkaround(), TinyG");
            if (PumpIsOn)
            {
                if (RawWrite(command))
                {
                    Thread.Sleep(50);
                    PumpIsOn = false;
                }
            }
            else if (Controlboard == ControlBoardType.qQuintic)
            {
                MainForm.DisplayText("PumpOff_NoWorkaround(), qQuintic  -- SKIPPED --");
            }
            else
            {
                MainForm.DisplayText("PumpOff_NoWorkaround(), qQuintic  -- SKIPPED --");
            }
            MainForm.Pump_checkBox.Checked = PumpIsOn;
        }

        // =================================================================================
        StringBuilder holdline = new StringBuilder();
        bool getSettings = false;
        public void InterpretLine(string line)
        {
            // This is called from SerialComm dataReceived, and runs in a separate thread than UI            
            //MainForm.DisplayText("<== " + line);

            //if (line.ToLower().Contains("error"))
            //{
            //    MainForm.DisplayText("error code thrown error = " + line);
            //    Error();
            //    return;
            //}
            if (line.ToLower().Contains("ok") && getSettings)
            {
                getSettings = false;
                ParameterValue(line);  // <========= causes UI update
                holdline = null;
                return;
            }

            if (line.ToLower().StartsWith("<idle"))
            {
                // MainForm.DisplayText("ReadyEvent ok");
                _readyEvent.Set();
                return;
            }

            if (line.ToLower().StartsWith("<alarm"))
            {
                MainForm.DisplayText("System in Alarm Status");
                _readyEvent.Set();
                return;
            }
            if (line.ToLower().StartsWith("message:"))
            {
                MainForm.DisplayText(line);
                _readyEvent.Set();
                return;
            }
            //if (line.ToLower().Contains("wpos:"))
            //{
            //    GetPosition(line);
            //    return;
            //}

            if (line.Contains("$"))
            {
                getSettings = true;
                ParameterValue(line);
                //_readyEvent.Set();
                return;
            }

        }  // end InterpretLine()

        public void ParameterValue(string line)
        {
            List<GRBLSettings> settings = new List<GRBLSettings>();
            try
            {
                
                string temp = line.Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' ').Trim();
                string[] sets = temp.Split('$');
                foreach (string st in sets)
                {
                    if (st.Equals(""))
                        continue;
                    string[] tst1 = st.Split('=');
                    GRBLSettings set = new GRBLSettings("$" + tst1[0], tst1[1]);
                    settings.Add(set);
                }
            }
            catch(Exception ex)
            {
            }
            MainForm.ValueUpdater(settings);

        }
        public bool updatePos = false;
        public void GetPosition(string line)
        {
            if (updatePos)
            {
                string line1 = line.Substring(line.IndexOf("wpos:") + 5);
                string[] sections = line1.Split('|');
                int start = sections[1].IndexOf(':');
                string positions = sections[1].Substring(start + 1);
                string[] pos = positions.Split(',');
                CNC.setCurrX(Convert.ToDouble(pos[0]));
                CNC.setCurrY(Convert.ToDouble(pos[1]));
                CNC.setCurrZ(Convert.ToDouble(pos[2]));
                CNC.setCurrA(Convert.ToDouble(pos[3]));
                MainForm.Update_xpos(pos[0]);
                MainForm.Update_ypos(pos[1]);
                MainForm.Update_zpos(pos[2]);
                MainForm.Update_apos(pos[3]);
            }

        }
        // =================================================================================
        // TinyG JSON stuff
        // =================================================================================

        // =================================================================================
        // Status report


        [Serializable]
        public class StatusReport
        {
            public Sr sr { get; set; }
        }

        public StatusReport Status;
        public void NewStatusReport(string line)
        {
            //MainForm.DisplayText("NewStatusReport: " + line);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Status = serializer.Deserialize<StatusReport>(line);
        }

        [Serializable]
        public class Sr
        {
            // mpox, posy, ...: Position
            // NOTE: Some firmware versions use mpox, mpoy,... some use posx, posy, ... 
            // This should be reflected in the public variable names
            private double _posx = 0;
            public double posx // <======================== here
            {
                get { return _posx; }
                set
                {
                    _posx = value;
                    CNC.setCurrX(_posx);
                    //CNC.MainForm.ValueUpdater("posx", _posx.ToString("0.000", CultureInfo.InvariantCulture));
                }
            }

            private double _posy = 0;
            public double posy // <======================== and here
            {
                get { return _posy; }
                set
                {
                    _posy = value;
                    CNC.setCurrY(_posy);
                    //CNC.MainForm.ValueUpdater("posy", _posy.ToString("0.000", CultureInfo.InvariantCulture));
                }
            }

            private double _posz = 0;
            public double posz // <======================== and here
            {
                get { return _posz; }
                set
                {
                    _posz = value;
                    CNC.setCurrZ(_posz);
                    //CNC.MainForm.ValueUpdater("posz", _posz.ToString("0.000", CultureInfo.InvariantCulture));
                }
            }

            private double _posa = 0;
            public double posa // <======================== and here
            {
                get { return _posa; }
                set
                {
                    _posa = value;
                    CNC.setCurrA(_posa);
                    //CNC.MainForm.ValueUpdater("posa", _posa.ToString("0.000", CultureInfo.InvariantCulture));
                }
            }

        }

    }  // end Class CNC
}


