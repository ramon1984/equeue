﻿using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using EQueue.Infrastructure.IoC;
using EQueue.Infrastructure.Logging;
using EQueue.Protocols;

namespace EQueue.Broker
{
    public class MessageService : IMessageService
    {
        private const int DefaultTopicQueueCount = 4;
        private ConcurrentDictionary<string, long> _queueCurrentOffsetDict = new ConcurrentDictionary<string, long>();
        private ConcurrentDictionary<string, IList<Queue>> _queueDict = new ConcurrentDictionary<string, IList<Queue>>();
        private IQueueSelector _queueSelector;
        private IMessageStore _messageStore;
        private ILogger _logger;

        public MessageService(IQueueSelector messageQueueSelector, IMessageStore messageStore)
        {
            _queueSelector = messageQueueSelector;
            _messageStore = messageStore;
            _logger = ObjectContainer.Resolve<ILoggerFactory>().Create(GetType().Name);
        }

        public MessageStoreResult StoreMessage(Message message, string arg)
        {
            var queues = GetQueues(message.Topic);
            var queue = _queueSelector.SelectQueue(queues, message, arg);
            var queueOffset = queue.IncrementCurrentOffset();
            var storeResult = _messageStore.StoreMessage(message, queue.QueueId, queueOffset);
            queue.SetMessageOffset(queueOffset, storeResult.MessageOffset);
            _logger.DebugFormat("Message stored, offset:{0}, topic:{1}, queueId:{2}, queueOffset:{3}", storeResult.MessageOffset, message.Topic, queue.QueueId, queueOffset);
            return storeResult;
        }
        public IEnumerable<QueueMessage> GetMessages(string topic, int queueId, long queueOffset, int batchSize)
        {
            var queues = GetQueues(topic);
            var queue = queues.SingleOrDefault(x => x.QueueId == queueId);
            if (queue != null)
            {
                var messageOffset = queue.GetMessageOffset(queueOffset);
                if (messageOffset >= 0)
                {
                    var offset = messageOffset;
                    var messages = new List<QueueMessage>();
                    while (messages.Count < batchSize)
                    {
                        var message = _messageStore.GetMessage(offset);
                        if (message != null)
                        {
                            messages.Add(message);
                            offset++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    return messages;
                }
            }
            return new QueueMessage[0];
        }
        public long GetQueueCurrentOffset(string topic, int queueId)
        {
            var queues = GetQueues(topic);
            var queue = queues.SingleOrDefault(x => x.QueueId == queueId);
            if (queue != null)
            {
                return queue.CurrentOffset;
            }
            return -1;
        }
        public int GetTopicQueueCount(string topic)
        {
            return GetQueues(topic).Count;
        }

        private IList<Queue> GetQueues(string topic)
        {
            return _queueDict.GetOrAdd(topic, x =>
            {
                var queues = new List<Queue>();
                for (var index = 0; index < DefaultTopicQueueCount; index++)
                {
                    queues.Add(new Queue(x, index));
                }
                return queues;
            });
        }
    }
}
