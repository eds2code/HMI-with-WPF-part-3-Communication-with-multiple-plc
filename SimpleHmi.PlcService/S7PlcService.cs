﻿using Sharp7;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace SimpleHmi.PlcService
{
    public class S7PlcService : IPlcService
    {
        private readonly S7Client _client;
        private readonly System.Timers.Timer _timer;
        private DateTime _lastScanTime;

        private volatile object _locker = new object();

        public S7PlcService()
        {
            _client = new S7Client();
            _timer = new System.Timers.Timer();
            _timer.Interval = 100;
            _timer.Elapsed += OnTimerElapsed;
        }

        public ConnectionStates ConnectionState { get; private set; }

        public bool HighLimit { get; private set; }

        public bool LowLimit { get; private set; }

        public bool PumpState { get; private set; }

        public int TankLevel { get; private set; }

        public TimeSpan ScanTime { get; private set; }

        public int InletPumpSpeed { get; private set; }

        public int OutletPumpSpeed { get; private set; }

        public event EventHandler ValuesRefreshed;        

        public void Connect()
        {
            try
            {
                ConnectionState = ConnectionStates.Connecting;
                int result = _client.ConnectTo("127.0.0.1", 0, 1);
                if (result == 0)
                {
                    ConnectionState = ConnectionStates.Online;
                    _timer.Start();
                }
                else
                {
                    Debug.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "\t Connection error: " + _client.ErrorText(result));
                    ConnectionState = ConnectionStates.Offline;
                }
                OnValuesRefreshed();
            }
            catch
            {
                ConnectionState = ConnectionStates.Offline;
                OnValuesRefreshed();
                throw;
            }
        }

        public Task WriteSpeedInletPump(short speed)
        {
            return Task.Run(() => {
                var writeResult = WriteWord("DB1.DBW4", speed);
                if (writeResult != 0)
                {
                    Debug.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "\t Write error: " +
                                    _client.ErrorText(writeResult));
                }
            });

        }

        public Task WriteSpeedOutletPump(short speed)
        {
            return Task.Run(() => {
                var writeResult = WriteWord("DB1.DBW6", speed);
                if (writeResult != 0)
                {
                    Debug.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "\t Write error: " +
                                    _client.ErrorText(writeResult));
                }
            });
        }

        public void Disconnect()
        {
            if (_client.Connected)
            {
                _timer.Stop();
                _client.Disconnect();
                ConnectionState = ConnectionStates.Offline;
                OnValuesRefreshed();
            }
        }

        private int WriteWord(string address, short value)
        {
            var strings = address.Split('.');
            var db = Convert.ToInt32(strings[0].Replace("DB", ""));
            var pos = Convert.ToInt32(strings[1].Replace("DBW", ""));
            return WriteWord(db, pos, value);
        }

        private int WriteWord(int dbNumber, int startIndex, short value)
        {
            lock (_locker)
            {
                var buffer = new byte[2];
                S7.SetIntAt(buffer, 0, value);
                return _client.DBWrite(dbNumber, startIndex, buffer.Length, buffer);
            }
        }

        public async Task WriteStart()
        {
            await Task.Run(() =>
            {
                int writeResult = WriteBit("DB1.DBX0.0", true);
                if (writeResult != 0)
                {
                    Debug.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "\t Write error: " + _client.ErrorText(writeResult));
                }
                Thread.Sleep(30);
                writeResult = WriteBit("DB1.DBX0.0", false);
                if (writeResult != 0)
                {
                    Debug.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "\t Write error: " + _client.ErrorText(writeResult));
                }
            });
        }

        public async Task WriteStop()
        {
            await Task.Run(() =>
            {
                int writeResult = WriteBit("DB1.DBX0.1", true);
                if (writeResult != 0)
                {
                    Debug.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "\t Write error: " + _client.ErrorText(writeResult));
                }
                Thread.Sleep(30);
                writeResult = WriteBit("DB1.DBX0.1", false);
                if (writeResult != 0)
                {
                    Debug.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "\t Write error: " + _client.ErrorText(writeResult));
                }
            });
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                _timer.Stop();
                ScanTime = DateTime.Now - _lastScanTime;
                RefreshValues();
                OnValuesRefreshed();
            }
            finally
            {
                _timer.Start();
            }
            _lastScanTime = DateTime.Now;
        }

        private void RefreshValues()
        {
            lock (_locker)
            {
                var buffer = new byte[8];
                int result = _client.DBRead(1, 0, buffer.Length, buffer);
                if (result == 0)
                {
                    PumpState = S7.GetBitAt(buffer, 0, 2);
                    HighLimit = S7.GetBitAt(buffer, 0, 3);
                    LowLimit = S7.GetBitAt(buffer, 0, 4);
                    TankLevel = S7.GetIntAt(buffer, 2);
                    InletPumpSpeed = S7.GetIntAt(buffer, 4);
                    OutletPumpSpeed = S7.GetIntAt(buffer, 6);
                }
                else
                {
                    Debug.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "\t Read error: " + _client.ErrorText(result));
                }
            }
        }

        /// <summary>
        /// Writes a bit at the specified address. Es.: DB1.DBX10.2 writes the bit in db 1, word 10, 3rd bit
        /// </summary>
        /// <param name="address">Es.: DB1.DBX10.2 writes the bit in db 1, word 10, 3rd bit</param>
        /// <param name="value">true or false</param>
        /// <returns></returns>
        private int WriteBit(string address, bool value)
        {
            var strings = address.Split('.');
            int db = Convert.ToInt32(strings[0].Replace("DB", ""));
            int pos = Convert.ToInt32(strings[1].Replace("DBX", ""));
            int bit = Convert.ToInt32(strings[2]);
            return WriteBit(db, pos, bit, value);
        }

        private int WriteBit(int db, int pos, int bit, bool value)
        {
            lock (_locker)
            {
                var buffer = new byte[1];
                S7.SetBitAt(ref buffer, 0, bit, value);
                return _client.WriteArea(S7Consts.S7AreaDB, db, pos + bit, buffer.Length, S7Consts.S7WLBit, buffer);
            }
        }

        private void OnValuesRefreshed()
        {
            ValuesRefreshed?.Invoke(this, new EventArgs());
        }
    }
}

