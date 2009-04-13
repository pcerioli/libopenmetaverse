/*
 * Copyright (c) 2007-2009, openmetaverse.org
 * All rights reserved.
 *
 * - Redistribution and use in source and binary forms, with or without 
 *   modification, are permitted provided that the following conditions are met:
 *
 * - Redistributions of source code must retain the above copyright notice, this
 *   list of conditions and the following disclaimer.
 * - Neither the name of the openmetaverse.org nor the names 
 *   of its contributors may be used to endorse or promote products derived from
 *   this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" 
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF 
 * SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
 * CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
 * POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using OpenMetaverse.Packets;
using OpenMetaverse.Messages.Linden;
using OpenMetaverse.Interfaces;

namespace OpenMetaverse
{
    /// <summary>
    /// Registers, unregisters, and fires events generated by incoming packets
    /// </summary>
    public class PacketEventDictionary
    {
        /// <summary>
        /// Object that is passed to worker threads in the ThreadPool for
        /// firing packet callbacks
        /// </summary>
        private struct PacketCallbackWrapper
        {
            /// <summary>Callback to fire for this packet</summary>
            public NetworkManager.PacketCallback Callback;
            /// <summary>Reference to the simulator that this packet came from</summary>
            public Simulator Simulator;
            /// <summary>The packet that needs to be processed</summary>
            public Packet Packet;
        }

        /// <summary>Reference to the GridClient object</summary>
        public GridClient Client;

        private Dictionary<PacketType, NetworkManager.PacketCallback> _EventTable = 
            new Dictionary<PacketType,NetworkManager.PacketCallback>();
        private WaitCallback _ThreadPoolCallback;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="client"></param>
        public PacketEventDictionary(GridClient client)
        {
            Client = client;
            _ThreadPoolCallback = new WaitCallback(ThreadPoolDelegate);
        }

        /// <summary>
        /// Register an event handler
        /// </summary>
        /// <remarks>Use PacketType.Default to fire this event on every 
        /// incoming packet</remarks>
        /// <param name="packetType">Packet type to register the handler for</param>
        /// <param name="eventHandler">Callback to be fired</param>
        public void RegisterEvent(PacketType packetType, NetworkManager.PacketCallback eventHandler)
        {
            lock (_EventTable)
            {
                if (_EventTable.ContainsKey(packetType))
                    _EventTable[packetType] += eventHandler;
                else
                    _EventTable[packetType] = eventHandler;
            }
        }

        /// <summary>
        /// Unregister an event handler
        /// </summary>
        /// <param name="packetType">Packet type to unregister the handler for</param>
        /// <param name="eventHandler">Callback to be unregistered</param>
        public void UnregisterEvent(PacketType packetType, NetworkManager.PacketCallback eventHandler)
        {
            lock (_EventTable)
            {
                if (_EventTable.ContainsKey(packetType) && _EventTable[packetType] != null)
                    _EventTable[packetType] -= eventHandler;
            }
        }

        /// <summary>
        /// Fire the events registered for this packet type synchronously
        /// </summary>
        /// <param name="packetType">Incoming packet type</param>
        /// <param name="packet">Incoming packet</param>
        /// <param name="simulator">Simulator this packet was received from</param>
        internal void RaiseEvent(PacketType packetType, Packet packet, Simulator simulator)
        {
            NetworkManager.PacketCallback callback;

            // Default handler first, if one exists
            if (_EventTable.TryGetValue(PacketType.Default, out callback))
            {
                try { callback(packet, simulator); }
                catch (Exception ex)
                {
                    Logger.Log("Default packet event handler: " + ex.ToString(), Helpers.LogLevel.Error, Client);
                }
            }

            if (_EventTable.TryGetValue(packetType, out callback))
            {
                try { callback(packet, simulator); }
                catch (Exception ex)
                {
                    Logger.Log("Packet event handler: " + ex.ToString(), Helpers.LogLevel.Error, Client);
                }

                return;
            }
            
            if (packetType != PacketType.Default && packetType != PacketType.PacketAck)
            {
                Logger.DebugLog("No handler registered for packet event " + packetType, Client);
            }
        }

        /// <summary>
        /// Fire the events registered for this packet type asynchronously
        /// </summary>
        /// <param name="packetType">Incoming packet type</param>
        /// <param name="packet">Incoming packet</param>
        /// <param name="simulator">Simulator this packet was received from</param>
        internal void BeginRaiseEvent(PacketType packetType, Packet packet, Simulator simulator)
        {
            NetworkManager.PacketCallback callback;
            PacketCallbackWrapper wrapper;

            // Default handler first, if one exists
            if (_EventTable.TryGetValue(PacketType.Default, out callback))
            {
                if (callback != null)
                {
                    wrapper.Callback = callback;
                    wrapper.Packet = packet;
                    wrapper.Simulator = simulator;
                    ThreadPool.QueueUserWorkItem(_ThreadPoolCallback, wrapper);
                }
            }

            if (_EventTable.TryGetValue(packetType, out callback))
            {
                if (callback != null)
                {
                    wrapper.Callback = callback;
                    wrapper.Packet = packet;
                    wrapper.Simulator = simulator;
                    ThreadPool.QueueUserWorkItem(_ThreadPoolCallback, wrapper);

                    return;
                }
            }

            if (packetType != PacketType.Default && packetType != PacketType.PacketAck)
            {
                Logger.DebugLog("No handler registered for packet event " + packetType, Client);
            }
        }

        private void ThreadPoolDelegate(Object state)
        {
            PacketCallbackWrapper wrapper = (PacketCallbackWrapper)state;

            try
            {
                wrapper.Callback(wrapper.Packet, wrapper.Simulator);
            }
            catch (Exception ex)
            {
                Logger.Log("Async Packet Event Handler: " + ex.ToString(), Helpers.LogLevel.Error, Client);
            }
        }
    }

    /// <summary>
    /// Registers, unregisters, and fires events generated by the Capabilities
    /// event queue
    /// </summary>
    public class CapsEventDictionary
    {
        /// <summary>
        /// Object that is passed to worker threads in the ThreadPool for
        /// firing CAPS callbacks
        /// </summary>
        private struct CapsCallbackWrapper
        {
            /// <summary>Callback to fire for this packet</summary>
            public Caps.EventQueueCallback Callback;
            /// <summary>Name of the CAPS event</summary>
            public string CapsEvent;
            /// <summary>Strongly typed decoded data</summary>
            public IMessage Message;
            /// <summary>Reference to the simulator that generated this event</summary>
            public Simulator Simulator;
        }

        /// <summary>Reference to the GridClient object</summary>
        public GridClient Client;

        private Dictionary<string, Caps.EventQueueCallback> _EventTable =
            new Dictionary<string, Caps.EventQueueCallback>();
        private WaitCallback _ThreadPoolCallback;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="client">Reference to the GridClient object</param>
        public CapsEventDictionary(GridClient client)
        {
            Client = client;
            _ThreadPoolCallback = new WaitCallback(ThreadPoolDelegate);
        }

        /// <summary>
        /// Register an event handler
        /// </summary>
        /// <remarks>Use String.Empty to fire this event on every CAPS event</remarks>
        /// <param name="capsEvent">Capability event name to register the 
        /// handler for</param>
        /// <param name="eventHandler">Callback to fire</param>
        public void RegisterEvent(string capsEvent, Caps.EventQueueCallback eventHandler)
        {
            lock (_EventTable)
            {
                if (_EventTable.ContainsKey(capsEvent))
                    _EventTable[capsEvent] += eventHandler;
                else
                    _EventTable[capsEvent] = eventHandler;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="capsEvent">Capability event name unregister the 
        /// handler for</param>
        /// <param name="eventHandler">Callback to unregister</param>
        public void UnregisterEvent(string capsEvent, Caps.EventQueueCallback eventHandler)
        {
            lock (_EventTable)
            {
                if (_EventTable.ContainsKey(capsEvent) && _EventTable[capsEvent] != null)
                    _EventTable[capsEvent] -= eventHandler;
            }
        }

        /// <summary>
        /// Fire the events registered for this event type synchronously
        /// </summary>
        /// <param name="capsEvent">Capability name</param>
        /// <param name="body">Decoded event body</param>
        /// <param name="simulator">Reference to the simulator that 
        /// generated this event</param>
        internal void RaiseEvent(string capsEvent, IMessage message, Simulator simulator)
        {
            bool specialHandler = false;
            Caps.EventQueueCallback callback;
            
            // Default handler first, if one exists
            if (_EventTable.TryGetValue(capsEvent, out callback))
            {
                if (callback != null)
                {
                    try { callback(capsEvent, message, simulator); }
                    catch (Exception ex) { Logger.Log("CAPS Event Handler: " + ex.ToString(), Helpers.LogLevel.Error, Client); }
                }
            }

            // Generic parser next
            //if (body.Type == StructuredData.OSDType.Map)
            //{
            //    StructuredData.OSDMap map = (StructuredData.OSDMap)body;
            //    Packet packet = Packet.BuildPacket(capsEvent, map);
            //    if (packet != null)
            //    {
            //        NetworkManager.IncomingPacket incomingPacket;
            //        incomingPacket.Simulator = simulator;
            //        incomingPacket.Packet = packet;

            //        Logger.DebugLog("Serializing " + packet.Type.ToString() + " capability with generic handler", Client);

            //        Client.Network.PacketInbox.Enqueue(incomingPacket);
            //        specialHandler = true;
            //    }
            //}

            // Explicit handler next
            if (_EventTable.TryGetValue(capsEvent, out callback) && callback != null)
            {
                try { callback(capsEvent, message, simulator); }
                catch (Exception ex) { Logger.Log("CAPS Event Handler: " + ex.ToString(), Helpers.LogLevel.Error, Client); }

                specialHandler = true;
            }

            if (!specialHandler)
                Logger.Log("Unhandled CAPS event " + capsEvent, Helpers.LogLevel.Warning, Client);
        }

        /// <summary>
        /// Fire the events registered for this event type asynchronously
        /// </summary>
        /// <param name="capsEvent">Capability name</param>
        /// <param name="body">Decoded event body</param>
        /// <param name="simulator">Reference to the simulator that 
        /// generated this event</param>
        internal void BeginRaiseEvent(string capsEvent, IMessage message, Simulator simulator)
        {
            bool specialHandler = false;
            Caps.EventQueueCallback callback;

            // Default handler first, if one exists
            if (_EventTable.TryGetValue(String.Empty, out callback))
            {
                if (callback != null)
                {
                    CapsCallbackWrapper wrapper;
                    wrapper.Callback = callback;
                    wrapper.CapsEvent = capsEvent;
                    wrapper.Message = message;
                    wrapper.Simulator = simulator;
                    ThreadPool.QueueUserWorkItem(_ThreadPoolCallback, wrapper);
                }
            }

            // Generic parser next, don't generic parse events we've manually registered for
            //if (body.Type == StructuredData.OSDType.Map && !_EventTable.ContainsKey(capsEvent))
            //{
            //    StructuredData.OSDMap map = (StructuredData.OSDMap)body;
            //    Packet packet = Packet.BuildPacket(capsEvent, map);
                
            //    if (packet != null)
            //    {
            //        NetworkManager.IncomingPacket incomingPacket;
            //        incomingPacket.Simulator = simulator;
            //        incomingPacket.Packet = packet;

            //        Logger.DebugLog("Serializing " + packet.Type.ToString() + " capability with generic handler", Client);

            //        Client.Network.PacketInbox.Enqueue(incomingPacket);
            //        specialHandler = true;
            //    }
            //}
            
            // Explicit handler next
            if (_EventTable.TryGetValue(capsEvent, out callback) && callback != null)
            {
                CapsCallbackWrapper wrapper;
                wrapper.Callback = callback;
                wrapper.CapsEvent = capsEvent;
                wrapper.Message = message;
                wrapper.Simulator = simulator;
                ThreadPool.QueueUserWorkItem(_ThreadPoolCallback, wrapper);

                specialHandler = true;
            }

            if (!specialHandler)
                Logger.Log("Unhandled CAPS event " + capsEvent, Helpers.LogLevel.Warning, Client);
        }

        private void ThreadPoolDelegate(Object state)
        {
            CapsCallbackWrapper wrapper = (CapsCallbackWrapper)state;

            try
            {
                wrapper.Callback(wrapper.CapsEvent, wrapper.Message, wrapper.Simulator);
            }
            catch (Exception ex)
            {
                Logger.Log("Async CAPS Event Handler: " + ex.ToString(), Helpers.LogLevel.Error, Client);
            }
        }
    }
}
