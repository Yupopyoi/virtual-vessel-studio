import io
import json
import os

import pytest

from voicelab import protocol


def run(tmp_path, operations, operation, request):
    path = tmp_path / "request.json"
    path.write_text(request if isinstance(request, str) else json.dumps(request), encoding="utf-8")
    out = io.StringIO()
    code = protocol.main(operations, [operation, str(path)], out)
    return code, [json.loads(text) for text in out.getvalue().splitlines()]


def test_a_successful_operation_reports_and_exits_zero(tmp_path):
    def hello(request, reporter):
        reporter.progress("greet", 1, 2)
        reporter.result(name=request["name"])

    code, lines = run(tmp_path, {"hello": hello}, "hello", {"name": "声"})

    assert code == protocol.EXIT_OK
    assert lines == [{"type": "progress", "stage": "greet", "done": 1, "total": 2}, {"type": "result", "name": "声"}]


@pytest.mark.parametrize("request_text", ['{"name": ', '[1, 2]'])
def test_a_request_that_is_not_an_object_is_a_bad_request(tmp_path, request_text):
    code, lines = run(tmp_path, {"hello": lambda r, p: None}, "hello", request_text)

    assert code == protocol.EXIT_BAD_REQUEST
    assert lines[-1]["code"] == "bad_request"


def test_an_unknown_operation_is_a_bad_request(tmp_path):
    code, lines = run(tmp_path, {"hello": lambda r, p: None}, "bye", {})

    assert code == protocol.EXIT_BAD_REQUEST
    assert lines[-1]["type"] == "error"


def test_a_missing_item_names_the_item(tmp_path):
    code, lines = run(tmp_path, {"hello": lambda r, p: protocol.require(r, "source", str)}, "hello", {})

    assert code == protocol.EXIT_BAD_REQUEST
    assert "'source'" in lines[-1]["message"]


@pytest.mark.parametrize("exception, code", [
    (FileNotFoundError("x.wav"), "missing_file"),
    (MemoryError(), "out_of_memory"),
    (type("OutOfMemoryError", (RuntimeError,), {})(), "out_of_memory"),
    (RuntimeError("boom"), "failed"),
])
def test_failures_map_to_codes(exception, code):
    assert protocol.error_code(exception) == code


def test_a_failure_exits_one_after_an_error_line(tmp_path):
    def fail(request, reporter):
        raise RuntimeError("boom")

    code, lines = run(tmp_path, {"fail": fail}, "fail", {})

    assert code == protocol.EXIT_FAILED
    assert lines[-1] == {"type": "error", "code": "failed", "message": "RuntimeError: boom"}


def test_an_atomic_path_appears_only_when_finished(tmp_path):
    target = tmp_path / "out" / "voice.wav"
    with pytest.raises(RuntimeError):
        with protocol.atomic_path(str(target)) as temporary:
            open(temporary, "wb").write(b"half")
            raise RuntimeError("stopped")

    assert not target.exists()
    assert os.listdir(tmp_path / "out") == []

    with protocol.atomic_path(str(target)) as temporary:
        open(temporary, "wb").write(b"whole")

    assert target.read_bytes() == b"whole"
    assert os.listdir(tmp_path / "out") == ["voice.wav"]
