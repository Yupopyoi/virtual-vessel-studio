"""How Unity runs a script (Voice Lab detailed design 8.5): an operation and a request file in, JSON Lines out.

Exit codes: 0 success, 1 failure (after an "error" line), 2 a bad request.
"""
import json
import os
import sys
import tempfile
import traceback
from contextlib import contextmanager, redirect_stdout

EXIT_OK = 0
EXIT_FAILED = 1
EXIT_BAD_REQUEST = 2


class BadRequest(Exception):
    """The request is missing an item or has a wrong value."""


def line(kind, **fields):
    """One JSON Lines record. Pure."""
    return json.dumps({"type": kind, **fields}, ensure_ascii=False, separators=(",", ":"))


def error_code(exception):
    """The error line's code for an exception. Pure."""
    if isinstance(exception, (BadRequest, json.JSONDecodeError)):
        return "bad_request"
    if isinstance(exception, FileNotFoundError):
        return "missing_file"
    if isinstance(exception, MemoryError) or type(exception).__name__ == "OutOfMemoryError":
        return "out_of_memory"
    return "failed"


def require(request, key, kind=None):
    """A request item, checked for presence and type. Pure."""
    if key not in request:
        raise BadRequest(f"The request has no '{key}'.")
    value = request[key]
    if kind is not None and not isinstance(value, kind):
        raise BadRequest(f"'{key}' must be {kind.__name__ if isinstance(kind, type) else kind}.")
    return value


class Reporter:
    """Writes progress and results to standard output, one flushed line each, so Unity sees them at once."""

    def __init__(self, stream=None):
        # Taken now, before main() points sys.stdout at the log.
        self._stream = stream or sys.stdout

    def progress(self, stage, done, total):
        self._write(line("progress", stage=stage, done=done, total=total))

    def result(self, **fields):
        self._write(line("result", **fields))

    def error(self, code, message):
        self._write(line("error", code=code, message=message))

    def _write(self, text):
        self._stream.write(text + "\n")
        self._stream.flush()


@contextmanager
def atomic_path(path):
    """A temporary path beside `path`, renamed to it only when the block finishes, so a stopped run leaves no
    half-made file."""
    folder = os.path.dirname(os.path.abspath(path))
    os.makedirs(folder, exist_ok=True)
    handle, temporary = tempfile.mkstemp(dir=folder, prefix=".part-", suffix=os.path.splitext(path)[1])
    os.close(handle)
    try:
        yield temporary
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.remove(temporary)


def main(operations, argv=None, stdout=None):
    """Runs `<operation> <request.json>`: operations maps a name to a function(request, reporter)."""
    argv = sys.argv[1:] if argv is None else argv
    reporter = Reporter(stdout)
    try:
        if len(argv) != 2 or argv[0] not in operations:
            raise BadRequest(f"Usage: <{'|'.join(sorted(operations))}> <request.json>")
        with open(argv[1], encoding="utf-8") as file:
            request = json.load(file)
        if not isinstance(request, dict):
            raise BadRequest("The request must be a JSON object.")
        # Libraries print to standard output (VoxCPM2's loading messages, RVC's logs); that is the log, and
        # standard output is kept for the JSON Lines alone.
        with redirect_stdout(sys.stderr):
            operations[argv[0]](request, reporter)
        return EXIT_OK
    except Exception as exception:  # Every failure becomes an error line; the traceback goes to the log.
        code = error_code(exception)
        reporter.error(code, f"{type(exception).__name__}: {exception}")
        if code != "bad_request":
            traceback.print_exc(file=sys.stderr)
        return EXIT_BAD_REQUEST if code == "bad_request" else EXIT_FAILED
