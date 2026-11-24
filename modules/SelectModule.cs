using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/selectmodule.c (CPython 3.12.15) as the Windows build has it: select.select and
    /// select.error, nothing else (poll, epoll, kqueue and devpoll are not compiled there). Only
    /// sockets can be selected, as with Winsock's select; the descriptors are _socket's numbers
    /// (SocketHandles) and the wait is Socket.Select.
    /// </summary>
    internal static class SelectModule
    {
        // FD_SETSIZE of the Windows build (PC/pyconfig.h sets 512).
        private const int FD_SETSIZE = 512;

        internal static PyModule Create()
        {
            var module = new PyModule("select");
            var d = module.ModuleDict;
            // _select_exec: select.error is OSError.
            d["error"] = PyType.OSErrorType;
            // select_methods
            d["select"] = PyArgs.FastCall("select", "select", 3, 4, args => Select(args[0], args[1], args[2], args.Length > 3 ? args[3] : PyNone.Instance));
            return module;
        }

        /// <summary>
        /// PyObject_AsFileDescriptor: an int, or what fileno() returns; a C int, not negative.
        /// </summary>
        internal static int AsFileDescriptor(PyObject o)
        {
            int fd;
            if (o is PyInt)
            {
                fd = PyArgs.Int(o);
            }
            else if (o.LookupAttribute("fileno") is { } meth)
            {
                var fno = meth.Call(Array.Empty<PyObject>(), null);
                if (fno is not PyInt)
                    throw PyTypeError.Create("fileno() returned a non-integer");
                fd = PyArgs.Int(fno);
            }
            else
            {
                throw PyTypeError.Create("argument must be an int, or have a fileno() method.");
            }
            if (fd < 0)
                throw PyValueError.Create($"file descriptor cannot be a negative integer ({fd})");
            return fd;
        }

        // seq2set: the objects and their descriptors.
        private static List<(PyObject Obj, int Fd)> Seq2Set(PyObject seq)
        {
            PyObject[] items;
            if (seq is PyList list)
                items = list.Items;
            else if (seq is PyTuple tuple)
                items = tuple.Items;
            else
            {
                // PySequence_Fast(seq, "arguments 1-3 must be sequences")
                try
                {
                    items = ((PyList)PyType.ListType.Call(new[] { seq }, null)).Items;
                }
                catch (PythonException failure) when (failure.PyException is PyTypeError)
                {
                    throw PyTypeError.Create("arguments 1-3 must be sequences");
                }
            }
            var result = new List<(PyObject, int)>();
            foreach (var o in items)
            {
                int v = AsFileDescriptor(o);
                if (result.Count >= FD_SETSIZE)
                    throw PyValueError.Create("too many file descriptors in select()");
                result.Add((o, v));
            }
            return result;
        }

        // select_select_impl
        private static PyObject Select(PyObject rlist, PyObject wlist, PyObject xlist, PyObject timeoutObj)
        {
            long timeout = 0;
            bool infinite = timeoutObj is PyNone;
            if (!infinite)
            {
                try
                {
                    timeout = PyTime.FromSecondsObject(timeoutObj, PyTimeRound.Timeout);
                }
                catch (PythonException failure) when (failure.PyException is PyTypeError)
                {
                    throw PyTypeError.Create("timeout must be a float or None");
                }
                var tv = PyTime.AsTimeval(timeout, PyTimeRound.Timeout);
                if (tv.Sec < 0)
                    throw PyValueError.Create("timeout must be non-negative");
            }
            var rfd = Seq2Set(rlist);
            var wfd = Seq2Set(wlist);
            var efd = Seq2Set(xlist);

            // FD_SET: the sockets behind the descriptors, each once.
            int error = 0;
            List<Socket> Sockets(List<(PyObject Obj, int Fd)> fds)
            {
                var sockets = new List<Socket>();
                foreach (var (_, fd) in fds)
                {
                    var sock = SocketHandles.Get(fd);
                    if (sock == null)
                        error = SocketModule.WSAENOTSOCK;
                    else if (!sockets.Contains(sock))
                        sockets.Add(sock);
                }
                return sockets;
            }
            var reads = Sockets(rfd);
            var writes = Sockets(wfd);
            var errors = Sockets(efd);
            // Winsock's select with nothing to wait for is WSAEINVAL.
            if (error == 0 && reads.Count == 0 && writes.Count == 0 && errors.Count == 0)
                error = SocketModule.WSAEINVAL;
            if (error == 0)
            {
                long deadline = infinite ? 0 : PyTime.GetMonotonicClock() + timeout;
                while (true)
                {
                    int micro = -1;
                    bool more = false;
                    if (!infinite)
                    {
                        long remaining = Math.Max(0, deadline - PyTime.GetMonotonicClock());
                        long us = PyTime.AsMicroseconds(remaining, PyTimeRound.Ceiling);
                        // Socket.Select counts in an int: a longer wait is made of several.
                        if (us > int.MaxValue)
                        {
                            us = int.MaxValue;
                            more = true;
                        }
                        micro = (int)us;
                    }
                    var r = new List<Socket>(reads);
                    var w = new List<Socket>(writes);
                    var e = new List<Socket>(errors);
                    PyGil.AllowThreads(() =>
                    {
                        try
                        {
                            Socket.Select(r.Count > 0 ? r : null, w.Count > 0 ? w : null, e.Count > 0 ? e : null, micro);
                        }
                        catch (SocketException failure)
                        {
                            error = SocketModule.ErrorCodeOf(failure);
                        }
                        catch (ObjectDisposedException)
                        {
                            error = SocketModule.WSAENOTSOCK;
                        }
                        catch (ArgumentException)
                        {
                            error = SocketModule.WSAEINVAL;
                        }
                    });
                    if (error != 0)
                        break;
                    if (r.Count + w.Count + e.Count > 0 || !more)
                    {
                        reads = r;
                        writes = w;
                        errors = e;
                        break;
                    }
                }
            }
            if (error != 0)
                throw SocketModule.SetError(error);
            // set2list
            PyList Set2List(List<(PyObject Obj, int Fd)> fds, List<Socket> ready)
            {
                var list = new PyList();
                foreach (var (obj, fd) in fds)
                {
                    var sock = SocketHandles.Get(fd);
                    if (sock != null && ready.Contains(sock))
                        list.Append(obj);
                }
                return list;
            }
            return PyTuple.From(Set2List(rfd, reads), Set2List(wfd, writes), Set2List(efd, errors));
        }
    }
}
