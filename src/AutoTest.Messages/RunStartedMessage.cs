using System;
namespace AutoTest.Messages
{
	public class RunStartedMessage : IMessage
    {
        private ChangedFile[] _files;

        public ChangedFile[] Files { get { return _files; } }

        public RunStartedMessage(ChangedFile[] files)
        {
            _files = files;
        }
    }
}
