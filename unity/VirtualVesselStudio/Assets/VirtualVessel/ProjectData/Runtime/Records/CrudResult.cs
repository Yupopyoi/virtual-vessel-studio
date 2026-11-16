using System;
using System.IO;

namespace VirtualVessel.ProjectData.Records
{
    /// <summary>Why a CRUD operation did not succeed (system design 6.31).</summary>
    public enum CrudError
    {
        None,

        /// <summary>The record is not there.</summary>
        NotFound,

        /// <summary>It breaks a rule: an empty or too long name, an ID that is not one, and so on.</summary>
        Invalid,

        /// <summary>Not now: being made, in use, and so on.</summary>
        Busy,

        /// <summary>It could not be read or written: files, permissions, and so on.</summary>
        StorageFailed,
    }

    /// <summary>
    /// The result of a CRUD operation that returns no value (system design 6.31). The CRUD side only reports;
    /// the caller decides how to handle a failure. <see cref="Detail"/> is for developers.
    /// </summary>
    public readonly struct CrudResult
    {
        private CrudResult(CrudError error, string detail)
        {
            Error = error;
            Detail = detail ?? string.Empty;
        }

        public static CrudResult Success => default;

        public CrudError Error { get; }

        public string Detail { get; }

        public bool Ok => Error == CrudError.None;

        public static CrudResult Fail(CrudError error, string detail)
        {
            if (error == CrudError.None)
            {
                throw new ArgumentException("A failure needs a kind.", nameof(error));
            }

            return new CrudResult(error, detail);
        }

        /// <summary>Runs <paramref name="work"/>; reading or writing that fails is reported, not thrown.</summary>
        public static CrudResult Run(Action work, string what)
        {
            try
            {
                work();
                return Success;
            }
            catch (Exception exception) when (IsStorage(exception))
            {
                return Fail(CrudError.StorageFailed, what + ": " + exception.Message);
            }
        }

        /// <summary>Whether an exception is a failure to read or write, which CRUD reports, rather than a bug.</summary>
        public static bool IsStorage(Exception exception)
        {
            return exception is IOException || exception is UnauthorizedAccessException;
        }

        public override string ToString()
        {
            return Ok ? "Ok" : Error + ": " + Detail;
        }
    }

    /// <summary>The result of a CRUD operation that returns a value (system design 6.31).</summary>
    public readonly struct CrudResult<T>
    {
        private CrudResult(T value, CrudError error, string detail)
        {
            Value = value;
            Error = error;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Only meaningful when <see cref="Ok"/>.</summary>
        public T Value { get; }

        public CrudError Error { get; }

        public string Detail { get; }

        public bool Ok => Error == CrudError.None;

        /// <summary>The same result without its value.</summary>
        public CrudResult WithoutValue => Ok ? CrudResult.Success : CrudResult.Fail(Error, Detail);

        public static CrudResult<T> Success(T value)
        {
            return new CrudResult<T>(value, CrudError.None, null);
        }

        public static CrudResult<T> Fail(CrudError error, string detail)
        {
            if (error == CrudError.None)
            {
                throw new ArgumentException("A failure needs a kind.", nameof(error));
            }

            return new CrudResult<T>(default, error, detail);
        }

        /// <summary>A failure of another operation, passed on with its kind and detail.</summary>
        public static CrudResult<T> From(CrudResult failure)
        {
            return Fail(failure.Error, failure.Detail);
        }

        /// <summary>Runs <paramref name="work"/>; reading or writing that fails is reported, not thrown.</summary>
        public static CrudResult<T> Run(Func<T> work, string what)
        {
            try
            {
                return Success(work());
            }
            catch (Exception exception) when (CrudResult.IsStorage(exception))
            {
                return Fail(CrudError.StorageFailed, what + ": " + exception.Message);
            }
        }

        public override string ToString()
        {
            return Ok ? "Ok: " + Value : Error + ": " + Detail;
        }
    }
}
