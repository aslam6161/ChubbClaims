namespace ChubbClaims.Service.Exceptions;

/// <summary>
/// Document storage refused the request or could not be reached. Maps to 503: the claim itself is
/// fine and the user can try again; the message never carries the vendor's error text.
/// </summary>
public sealed class DocumentStorageException(string message, Exception? inner = null) : Exception(message, inner);
