﻿using System.ComponentModel;

namespace ACT.SpecialSpellTimer.Utility
{
    public static class BackgroundWorkerExntensions
    {
        public static void Cancel(
            this BackgroundWorker worker)
        {
            if (!worker.WorkerSupportsCancellation)
            {
                worker.WorkerSupportsCancellation = true;
            }

            worker.CancelAsync();
        }
    }
}
