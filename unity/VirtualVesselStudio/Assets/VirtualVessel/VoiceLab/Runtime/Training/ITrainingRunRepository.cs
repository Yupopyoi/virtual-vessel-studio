using System.Collections.Generic;

namespace VirtualVessel.VoiceLab.Training
{
    /// <summary>
    /// Where Training Runs are kept (Voice Lab detailed design 6.3, 8.6). The first one keeps run.json files;
    /// SQLite (system design 13.12) will be another, so nothing else reads the records. A run's large files stay
    /// in its folder either way (system design 13.13).
    /// </summary>
    public interface ITrainingRunRepository
    {
        /// <summary>The folder of a run's files: its speech, weights, and output.</summary>
        string FolderOf(string runId);

        TrainingRun Create(RunDesign design);

        TrainingRun Save(TrainingRun run);

        /// <summary>The run, or null when there is none.</summary>
        TrainingRun Get(string runId);

        /// <summary>Every run that can be read, newest first.</summary>
        IReadOnlyList<TrainingRun> List();

        /// <summary>Deletes the run and its files; a run that is not there is no error.</summary>
        void Delete(string runId);
    }
}
