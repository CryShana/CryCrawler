using System;
using System.Collections.Generic;
using System.Text;

namespace CryCrawler.Worker
{
    public class WorkerProgram
    {
        readonly Configuration configuration;

        public WorkerProgram(Configuration config)
        {
            configuration = config;
        }

        public void Start()
        {
            Logger.Log("I am worker");
        }
    }
}
