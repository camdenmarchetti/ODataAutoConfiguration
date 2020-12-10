using System;
using System.Runtime.Serialization;

namespace ODataAutoConfiguration
{
  [Serializable]
  internal class ODataScaffoldException : Exception
  {
    /// <inheritdoc />
    public ODataScaffoldException() 
      : base() { }

    /// <inheritdoc />
    public ODataScaffoldException(string message) 
      : base(message) { }

    /// <inheritdoc />
    public ODataScaffoldException(string message, Exception innerException) 
      : base(message, innerException) { }

    /// <inheritdoc />
    protected ODataScaffoldException(SerializationInfo info, StreamingContext context) 
      : base(info, context) { }
  }
}
