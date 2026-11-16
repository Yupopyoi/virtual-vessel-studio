from voicelab import rvc


def test_steps_run_rvc_in_its_order_with_the_requested_epochs():
    steps = rvc.steps("D:/speech", "R/logs/vvs-1", "vvs-1", 150, 8)

    assert [stage for stage, _ in steps] == ["preprocess", "pitch", "features", "train"]
    train = steps[-1][1]
    assert train[0] == "train/train.py"
    assert train[train.index("-te") + 1] == "150" and train[train.index("-se") + 1] == "150"
    assert train[train.index("-e") + 1] == "vvs-1"


def test_only_slices_with_every_feature_are_trained():
    listings = {"0_gt_wavs": ["0_0.wav", "0_1.wav"], "3_feature768": ["0_0.npy", "0_1.npy"], "2a_f0": ["0_0.wav.npy"], "2b-f0nsf": ["0_0.wav.npy", "0_1.wav.npy"]}

    assert rvc.complete_slices(listings) == {"0_0"}


def test_the_filelist_has_each_slice_and_two_silences_in_a_fixed_order():
    lines = rvc.filelist(r"R\logs\vvs-1", "R", {"0_0", "0_1"})

    assert len(lines) == 4
    assert sum("logs/mute/" in line for line in lines) == 2
    assert "R/logs/vvs-1/0_gt_wavs/0_0.wav|R/logs/vvs-1/3_feature768/0_0.npy|R/logs/vvs-1/2a_f0/0_0.wav.npy|R/logs/vvs-1/2b-f0nsf/0_0.wav.npy|0" in lines
    assert lines == rvc.filelist(r"R\logs\vvs-1", "R", {"0_0", "0_1"})


def test_epochs_are_read_from_the_log_in_any_language():
    assert rvc.epoch_of("INFO:vvs-1:====> Epoch: 12 [2026-11-03 19:34:10] | (0:00:03)") == 12
    assert rvc.epoch_of("INFO:vvs-1:====> エポック: 3 [2026-11-03 19:34:17] | (0:00:03)") == 3
    assert rvc.epoch_of("INFO:vvs-1:====> 训练轮次：7 [x]") == 7
    assert rvc.epoch_of("INFO:vvs-1:学習エポック: 1 [0%]") is None


def test_out_of_memory_is_recognized():
    assert rvc.OUT_OF_MEMORY.search("torch.OutOfMemoryError: CUDA out of memory. Tried to allocate")


def test_rvc_runs_with_its_folder_and_the_stand_in_on_the_path():
    env = rvc.environment({"PATH": "p", "PYTHONHOME": "x"}, "R", "S")

    assert env["PYTHONPATH"].split(";" if ";" in env["PYTHONPATH"] else ":") == ["R", "S"]
    assert "PYTHONHOME" not in env and env["PATH"] == "p"
    assert env["PYTHONSAFEPATH"] == "1"


def test_only_this_projects_leftovers_are_removed():
    # vvs-target is the prototype's own training in a developer's RVC folder: it must survive.
    assert rvc.leftovers(["mute", "vvs-target", "vvs-lab-1a2b", "vvs-lab-1a2b.pth", "airy-raw"]) == ["vvs-lab-1a2b", "vvs-lab-1a2b.pth"]
