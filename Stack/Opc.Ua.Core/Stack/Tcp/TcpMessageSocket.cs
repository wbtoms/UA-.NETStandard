/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Creates a transport channel with UA-TCP transport, UA-SC security and UA Binary encoding
    /// </summary>
    public class TcpTransportChannel : UaSCUaBinaryTransportChannel
    {
        /// <summary>
        /// Create a Tcp transport channel.
        /// </summary>
        public TcpTransportChannel() :
            base(new TcpMessageSocketFactory())
        {
        }
    }

    /// <summary>
    /// Creates a new TcpTransportChannel with ITransportChannel interface.
    /// </summary>
    public class TcpTransportChannelFactory : ITransportChannelFactory
    {
        /// <summary>
        /// The protocol supported by the channel.
        /// </summary>
        public string UriScheme => Utils.UriSchemeOpcTcp;

        /// <summary>
        /// The method creates a new instance of a TCP transport channel
        /// </summary>
        /// <returns>The transport channel</returns>
        public ITransportChannel Create()
        {
            return new TcpTransportChannel();
        }
    }

    /// <summary>
    /// Handles async event callbacks from a socket
    /// </summary>
    public class TcpMessageSocketAsyncEventArgs : IMessageSocketAsyncEventArgs
    {
        /// <summary>
        /// Create the event args for the async TCP message socket.
        /// </summary>
        public TcpMessageSocketAsyncEventArgs()
        {
            m_args = new SocketAsyncEventArgs {
                UserToken = this
            };
        }

        #region IDisposable Members
        /// <inheritdoc/>
        public void Dispose()
        {
            m_args.Dispose();
        }
        #endregion

        /// <inheritdoc/>
        public object UserToken { get; set; }

        /// <inheritdoc/>
        public void SetBuffer(byte[] buffer, int offset, int count)
        {
            m_args.SetBuffer(buffer, offset, count);
        }

        /// <inheritdoc/>
        public bool IsSocketError => m_args.SocketError != SocketError.Success;

        /// <inheritdoc/>
        public string SocketErrorString => m_args.SocketError.ToString();

        /// <inheritdoc/>
        public event EventHandler<IMessageSocketAsyncEventArgs> Completed
        {
            add
            {
                m_InternalComplete += value;
                m_args.Completed += OnComplete;
            }
            remove
            {
                m_InternalComplete -= value;
                m_args.Completed -= OnComplete;
            }
        }

        /// <inheritdoc/>
        protected void OnComplete(object sender, SocketAsyncEventArgs e)
        {
            if (e.UserToken == null)
            {
                return;
            }

            m_InternalComplete(this, e.UserToken as IMessageSocketAsyncEventArgs);
        }

        /// <inheritdoc/>
        public int BytesTransferred => m_args.BytesTransferred;

        /// <inheritdoc/>
        public byte[] Buffer => m_args.Buffer;

        /// <inheritdoc/>
        public BufferCollection BufferList
        {
            get { return m_args.BufferList as BufferCollection; }
            set { m_args.BufferList = value; }
        }

        /// <summary>
        /// The socket event args.
        /// </summary>
        public SocketAsyncEventArgs Args => m_args;

        private SocketAsyncEventArgs m_args;
        private event EventHandler<IMessageSocketAsyncEventArgs> m_InternalComplete;
    }

    /// <summary>
    /// Handles async event callbacks only for the ConnectAsync method
    /// </summary>
    public class TcpMessageSocketConnectAsyncEventArgs : IMessageSocketAsyncEventArgs
    {
        /// <summary>
        /// Create the async event args for a TCP message socket.
        /// </summary>
        /// <param name="error">The socket error.</param>
        public TcpMessageSocketConnectAsyncEventArgs(SocketError error)
        {
            m_socketError = error;
        }

        #region IDisposable Members
        /// <inheritdoc/>
        public void Dispose()
        {
        }
        #endregion

        /// <inheritdoc/>
        public object UserToken { get; set; }

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public void SetBuffer(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public bool IsSocketError => m_socketError != SocketError.Success;

        /// <inheritdoc/>
        public string SocketErrorString => m_socketError.ToString();

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public event EventHandler<IMessageSocketAsyncEventArgs> Completed
        {
            add
            {
                throw new NotImplementedException();
            }
            remove
            {
                throw new NotImplementedException();
            }
        }

        /// <inheritdoc/>
        public int BytesTransferred => 0;

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public byte[] Buffer => null;

        /// <inheritdoc/>
        /// <remarks>Not implememnted here.</remarks>
        public BufferCollection BufferList
        {
            get { return null; }
            set
            {
                throw new NotImplementedException();
            }
        }

        private SocketError m_socketError;
    }

    /// <summary>
    /// Creates a new TcpMessageSocket with IMessageSocket interface.
    /// </summary>
    public class TcpMessageSocketFactory : IMessageSocketFactory
    {
        /// <summary>
        /// The method creates a new instance of a UA-TCP message socket
        /// </summary>
        /// <returns> the message socket</returns>
        public IMessageSocket Create(
                IMessageSink sink,
                BufferManager bufferManager,
                int receiveBufferSize
            )
        {
            return new TcpMessageSocket(sink, bufferManager, receiveBufferSize);
        }

        /// <summary>
        /// Gets the implementation description.
        /// </summary>
        /// <value>The implementation string.</value>
        public string Implementation => "UA-TCP";
    }

    /// <summary>
    /// Handles reading and writing of message chunks over a socket.
    /// </summary>
    public class TcpMessageSocket : IMessageSocket
    {
        #region Constructors
        /// <summary>
        /// Creates an unconnected socket.
        /// </summary>
        public TcpMessageSocket(
            IMessageSink sink,
            BufferManager bufferManager,
            int receiveBufferSize)
        {
            if (bufferManager == null) throw new ArgumentNullException(nameof(bufferManager));

            m_sink = sink;
            m_socket = null;
            m_bufferManager = bufferManager;
            m_receiveBufferSize = receiveBufferSize;
            m_incomingMessageSize = -1;
            m_readComplete = OnReadComplete;
            m_readState = ReadState.Ready;
        }

        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        public TcpMessageSocket(
            IMessageSink sink,
            Socket socket,
            BufferManager bufferManager,
            int receiveBufferSize)
        {
            if (socket == null) throw new ArgumentNullException(nameof(socket));
            if (bufferManager == null) throw new ArgumentNullException(nameof(bufferManager));

            m_sink = sink;
            m_socket = socket;
            m_bufferManager = bufferManager;
            m_receiveBufferSize = receiveBufferSize;
            m_incomingMessageSize = -1;
            m_readComplete = OnReadComplete;
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_socket?.Dispose();
            }
        }
        #endregion

        #region Connect/Disconnect Handling
        /// <summary>
        /// Gets the socket handle.
        /// </summary>
        /// <value>The socket handle.</value>
        public int Handle => m_socket?.GetHashCode() ?? -1;

        /// <summary>
        /// Gets the local endpoint.
        /// </summary>
        /// <exception cref="System.Net.Sockets.SocketException">An error occurred when attempting to access the socket.
        /// See the Remarks section for more information.</exception>
        /// <exception cref="System.ObjectDisposedException">The System.Net.Sockets.Socket has been closed.</exception>
        /// <returns>The System.Net.EndPoint that the System.Net.Sockets.Socket is using for communications.</returns>
        public EndPoint LocalEndpoint => m_socket?.LocalEndPoint;

        /// <summary>
        /// Gets the local endpoint.
        /// </summary>
        /// <exception cref="System.Net.Sockets.SocketException">An error occurred when attempting to access the socket.
        /// See the Remarks section for more information.</exception>
        /// <exception cref="System.ObjectDisposedException">The System.Net.Sockets.Socket has been closed.</exception>
        /// <returns>The System.Net.EndPoint that the System.Net.Sockets.Socket is using for communications.</returns>
        public EndPoint RemoteEndpoint => m_socket?.RemoteEndPoint;

        /// <summary>
        /// Gets the transport channel features implemented by this message socket.
        /// </summary>
        /// <value>The transport channel feature.</value>
        public TransportChannelFeatures MessageSocketFeatures => TransportChannelFeatures.ReverseConnect | TransportChannelFeatures.Reconnect;

        /// <summary>
        /// Connects to an endpoint.
        /// </summary>
        public bool BeginConnect(
            Uri endpointUrl,
            EventHandler<IMessageSocketAsyncEventArgs> callback,
            object state)
        {
            if (endpointUrl == null) throw new ArgumentNullException(nameof(endpointUrl));
            if (m_socket != null) throw new InvalidOperationException("The socket is already connected.");

            SocketError error = SocketError.NotInitialized;
            CallbackAction doCallback = (SocketError socketError) => callback(this, new TcpMessageSocketConnectAsyncEventArgs(socketError) { UserToken = state });

            // Get port
            int port = endpointUrl.Port;
            if (port <= 0 || port > UInt16.MaxValue)
            {
                port = Utils.UaTcpDefaultPort;
            }

            DnsEndPoint endpoint = new DnsEndPoint(endpointUrl.DnsSafeHost, port);
            error = BeginConnect(endpoint, doCallback);
            if (error == SocketError.InProgress || error == SocketError.Success)
            {
                return true;
            }


            return false;
        }

        /// <summary>
        /// Forcefully closes the socket.
        /// </summary>
        public void Close()
        {
            lock (m_socketLock)
            {
                m_closed = true;

                // Shutdown the socket.
                if (m_socket != null)
                {
                    try
                    {
                        if (m_socket.Connected)
                        {
                            m_socket.Shutdown(SocketShutdown.Both);
                        }
                    }
                    catch (Exception e)
                    {
                        Utils.LogError(e, "Unexpected error closing socket.");
                    }
                    finally
                    {
                        m_socket.Dispose();
                        m_socket = null;
                    }
                }
            }
        }
        #endregion

        #region Read Handling
        /// <summary>
        /// Starts reading messages from the socket.
        /// </summary>
        public void ReadNextMessage()
        {
            lock (m_readLock)
            {
                do
                {
                    // allocate a buffer large enough to a message chunk.
                    if (m_receiveBuffer == null)
                    {
                        m_receiveBuffer = m_bufferManager.TakeBuffer(m_receiveBufferSize, "ReadNextMessage");
                    }

                    // read the first 8 bytes of the message which contains the message size.          
                    m_bytesReceived = 0;
                    m_bytesToReceive = TcpMessageLimits.MessageTypeAndSize;
                    m_incomingMessageSize = -1;

                    do
                    {
                        ReadNextBlock();
                    } while (m_readState == ReadState.ReadNextBlock);
                } while (m_readState == ReadState.ReadNextMessage);
            }
        }

        /// <summary>
        /// Changes the sink used to report reads.
        /// </summary>
        public void ChangeSink(IMessageSink sink)
        {
            lock (m_readLock)
            {
                m_sink = sink;
            }
        }

        /// <summary>
        /// Handles a read complete event.
        /// </summary>
        private void OnReadComplete(object sender, SocketAsyncEventArgs e)
        {
            lock (m_readLock)
            {
                ServiceResult error = null;

                try
                {
                    bool innerCall = m_readState == ReadState.ReadComplete;
                    error = DoReadComplete(e);
                    // to avoid recursion, inner calls of OnReadComplete return
                    // after processing the ReadComplete and let the outer call handle it
                    if (!innerCall && !ServiceResult.IsBad(error))
                    {
                        while (ReadNext()) ;
                    }
                }
                catch (Exception ex)
                {
                    Utils.LogError(ex, "Unexpected error during OnReadComplete,");
                    error = ServiceResult.Create(ex, StatusCodes.BadTcpInternalError, ex.Message);
                }
                finally
                {
                    e?.Dispose();
                }

                if (m_readState == ReadState.NotConnected &&
                    ServiceResult.IsGood(error))
                {
                    error = ServiceResult.Create(StatusCodes.BadConnectionClosed, "Remote side closed connection.");
                }

                if (ServiceResult.IsBad(error))
                {
                    if (m_receiveBuffer != null)
                    {
                        m_bufferManager.ReturnBuffer(m_receiveBuffer, "OnReadComplete");
                        m_receiveBuffer = null;
                    }

                    m_sink?.OnReceiveError(this, error);
                }
            }
        }

        /// <summary>
        /// Handles a read complete event.
        /// </summary>
        private ServiceResult DoReadComplete(SocketAsyncEventArgs e)
        {
            // complete operation.
            int bytesRead = e.BytesTransferred;
            m_readState = ReadState.Ready;

            lock (m_socketLock)
            {
                BufferManager.UnlockBuffer(m_receiveBuffer);
            }

            if (bytesRead == 0)
            {
                // Remote end has closed the connection

                // free the empty receive buffer.
                if (m_receiveBuffer != null)
                {
                    m_bufferManager.ReturnBuffer(m_receiveBuffer, "DoReadComplete");
                    m_receiveBuffer = null;
                }

                m_readState = ReadState.Error;
                return ServiceResult.Create(StatusCodes.BadConnectionClosed, "Remote side closed connection");
            }

            m_bytesReceived += bytesRead;

            // check if more data left to read.
            if (m_bytesReceived < m_bytesToReceive)
            {
                m_readState = ReadState.ReadNextBlock;
                return ServiceResult.Good;
            }

            // start reading the message body.
            if (m_incomingMessageSize < 0)
            {
                UInt32 messageType = BitConverter.ToUInt32(m_receiveBuffer, 0);
                if (!TcpMessageType.IsValid(messageType))
                {
                    m_readState = ReadState.Error;

                    return ServiceResult.Create(
                        StatusCodes.BadTcpMessageTypeInvalid,
                        "Message type {0:X8} is invalid.",
                        messageType);
                }

                m_incomingMessageSize = BitConverter.ToInt32(m_receiveBuffer, 4);
                if (m_incomingMessageSize <= 0 || m_incomingMessageSize > m_receiveBufferSize)
                {
                    m_readState = ReadState.Error;

                    return ServiceResult.Create(
                        StatusCodes.BadTcpMessageTooLarge,
                        "Messages size {0} bytes is too large for buffer of size {1}.",
                        m_incomingMessageSize,
                        m_receiveBufferSize);
                }

                // set up buffer for reading the message body.
                m_bytesToReceive = m_incomingMessageSize;

                m_readState = ReadState.ReadNextBlock;

                return ServiceResult.Good;
            }

            // notify the sink.
            if (m_sink != null)
            {
                try
                {
                    // send notification (implementor responsible for freeing buffer) on success.
                    ArraySegment<byte> messageChunk = new ArraySegment<byte>(m_receiveBuffer, 0, m_incomingMessageSize);

                    // must allocate a new buffer for the next message.
                    m_receiveBuffer = null;

                    m_sink.OnMessageReceived(this, messageChunk);
                }
                catch (Exception ex)
                {
                    Utils.LogError(ex, "Unexpected error invoking OnMessageReceived callback.");
                }
            }

            // free the receive buffer.
            if (m_receiveBuffer != null)
            {
                m_bufferManager.ReturnBuffer(m_receiveBuffer, "DoReadComplete");
                m_receiveBuffer = null;
            }

            // start receiving next message.
            m_readState = ReadState.ReadNextMessage;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Reads the next block of data from the socket.
        /// </summary>
        private void ReadNextBlock()
        {
            Socket socket = null;

            // check if already closed.
            lock (m_socketLock)
            {
                socket = m_socket;

                if (socket == null || !socket.Connected)
                {
                    // buffer is returned in calling code
                    m_readState = ReadState.NotConnected;
                    return;
                }
            }

            BufferManager.LockBuffer(m_receiveBuffer);

            var args = new SocketAsyncEventArgs();
            try
            {
                m_readState = ReadState.Receive;
                args.SetBuffer(m_receiveBuffer, m_bytesReceived, m_bytesToReceive - m_bytesReceived);
                args.Completed += m_readComplete;
                if (!socket.ReceiveAsync(args))
                {
                    // I/O completed synchronously
                    if (args.SocketError != SocketError.Success)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadTcpInternalError, args.SocketError.ToString());
                    }
                    // set state to inner complete
                    m_readState = ReadState.ReadComplete;
                    m_readComplete(null, args);
                }
            }
            catch (ServiceResultException)
            {
                args?.Dispose();
                BufferManager.UnlockBuffer(m_receiveBuffer);
                throw;
            }
            catch (Exception ex)
            {
                args?.Dispose();
                BufferManager.UnlockBuffer(m_receiveBuffer);
                throw ServiceResultException.Create(StatusCodes.BadTcpInternalError, ex, "BeginReceive failed.");
            }
        }

        /// <summary>
        /// Helper to read read next block or message based on current state.
        /// </summary>
        private bool ReadNext()
        {
            bool result = true;
            switch (m_readState)
            {
                case ReadState.ReadNextBlock: ReadNextBlock(); break;
                case ReadState.ReadNextMessage: ReadNextMessage(); break;
                default: result = false; break;
            }
            return result;
        }

        /// <summary>
        /// delegate to handle internal callbacks with socket error
        /// </summary>
        private delegate void CallbackAction(SocketError error);

        /// <summary>
        /// Try to connect to endpoint and do callback if connected successfully
        /// </summary>
        /// <param name="endpoint">The DNS endpoint</param>
        /// <param name="callback">Callback that must be executed if the connection would be established</param>
        private SocketError BeginConnect(DnsEndPoint endpoint, CallbackAction callback)
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp){
                NoDelay = true,
                LingerState = new LingerOption(true, 5),
            };

            var args = new SocketAsyncEventArgs() {
                UserToken = callback,
                RemoteEndPoint = endpoint,
            };
            args.Completed += OnSocketConnected;
            if (!socket.ConnectAsync(args))
            {
                // I/O completed synchronously
                OnSocketConnected(socket, args);
                return args.SocketError;
            }
            return SocketError.InProgress;
        }

        /// <summary>
        /// Handle socket connection event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void OnSocketConnected(object sender, SocketAsyncEventArgs args)
        {
            var socket = sender as Socket;
            bool success = false;

            lock (m_socketLock)
            {
                if (!m_closed && m_socket == null)
                {
                    if (args.SocketError == SocketError.Success)
                    {
                        m_socket = socket;
                        success = true;
                    }
                }
            }

            ((CallbackAction)args.UserToken)(args.SocketError);

            if (!success)
            {
                try
                {
                    if (socket.Connected)
                    {
                        socket.Shutdown(SocketShutdown.Both);
                    }
                }
                catch
                {
                    // socket.Shutdown may throw but can be ignored
                }
                finally
                {
                    socket.Dispose();
                }
            }
            args.Dispose();
        }
        #endregion

        #region Write Handling
        /// <summary>
        /// Sends a buffer.
        /// </summary>
        public bool SendAsync(IMessageSocketAsyncEventArgs args)
        {
            if (!(args is TcpMessageSocketAsyncEventArgs eventArgs))
            {
                throw new ArgumentNullException(nameof(args));
            }
            if (m_socket == null)
            {
                throw new InvalidOperationException("The socket is not connected.");
            }
            eventArgs.Args.SocketError = SocketError.NotConnected;
            return m_socket.SendAsync(eventArgs.Args);
        }
        #endregion

        #region Event factory
        /// <summary>
        /// Create event args for TcpMessageSocket.
        /// </summary>
        public IMessageSocketAsyncEventArgs MessageSocketEventArgs()
        {
            return new TcpMessageSocketAsyncEventArgs();
        }
        #endregion

        #region Private Fields
        private IMessageSink m_sink;
        private BufferManager m_bufferManager;
        private readonly int m_receiveBufferSize;
        private readonly EventHandler<SocketAsyncEventArgs> m_readComplete;

        private readonly object m_socketLock = new object();
        private Socket m_socket;
        private bool m_closed;

        /// <summary>
        /// States for the nested read handler.
        /// </summary>
        private enum ReadState
        {
            Ready = 0,
            ReadNextMessage = 1,
            ReadNextBlock = 2,
            Receive = 3,
            ReadComplete = 4,
            NotConnected = 5,
            Error = 0xff
        };
        private readonly object m_readLock = new object();
        private byte[] m_receiveBuffer;
        private int m_bytesReceived;
        private int m_bytesToReceive;
        private int m_incomingMessageSize;
        private ReadState m_readState;
        #endregion
    }
}
