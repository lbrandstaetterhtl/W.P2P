using System;

namespace W.P2P.Models;

public class ContactNotFound(string msg) : Exception(msg);

public class BrokenFrame(string msg) : Exception(msg);

public class HandshakeNotFound(string msg) : Exception(msg);

public class NoConnection(string msg) : Exception(msg);

public class KeyNotFound(string msg) : Exception(msg);