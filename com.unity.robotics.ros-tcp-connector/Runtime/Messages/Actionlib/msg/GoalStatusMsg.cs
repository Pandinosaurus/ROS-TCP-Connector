//Do not edit! This file was generated by Unity-ROS MessageGeneration.
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

namespace RosMessageTypes.Actionlib
{
    [Serializable]
    public class GoalStatusMsg : Message
    {
        public const string k_RosMessageName = "actionlib_msgs/GoalStatus";

        public GoalIDMsg goal_id;
        public byte status;
        public const byte PENDING = 0; //  The goal has yet to be processed by the action server.
        public const byte ACTIVE = 1; //  The goal is currently being processed by the action server.
        public const byte PREEMPTED = 2; //  The goal received a cancel request after it started executing
        //    and has since completed its execution (Terminal State).
        public const byte SUCCEEDED = 3; //  The goal was achieved successfully by the action server
        //    (Terminal State).
        public const byte ABORTED = 4; //  The goal was aborted during execution by the action server due
        //     to some failure (Terminal State).
        public const byte REJECTED = 5; //  The goal was rejected by the action server without being processed,
        //     because the goal was unattainable or invalid (Terminal State).
        public const byte PREEMPTING = 6; //  The goal received a cancel request after it started executing
        //     and has not yet completed execution.
        public const byte RECALLING = 7; //  The goal received a cancel request before it started executing, but
        //     the action server has not yet confirmed that the goal is canceled.
        public const byte RECALLED = 8; //  The goal received a cancel request before it started executing
        //     and was successfully cancelled (Terminal State).
        public const byte LOST = 9; //  An action client can determine that a goal is LOST. This should not
        //     be sent over the wire by an action server.
        //  Allow for the user to associate a string with GoalStatus for debugging.
        public string text;

        public GoalStatusMsg()
        {
            this.goal_id = new GoalIDMsg();
            this.status = 0;
            this.text = "";
        }

        public GoalStatusMsg(GoalIDMsg goal_id, byte status, string text)
        {
            this.goal_id = goal_id;
            this.status = status;
            this.text = text;
        }

        public static GoalStatusMsg Deserialize(MessageDeserializer deserializer) => new GoalStatusMsg(deserializer);

        private GoalStatusMsg(MessageDeserializer deserializer)
        {
            this.goal_id = GoalIDMsg.Deserialize(deserializer);
            deserializer.Read(out this.status);
            deserializer.Read(out this.text);
        }

        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(this.goal_id);
            serializer.Write(this.status);
            serializer.Write(this.text);
        }

        public override string ToString()
        {
            return "GoalStatusMsg: " +
            "\ngoal_id: " + goal_id.ToString() +
            "\nstatus: " + status.ToString() +
            "\ntext: " + text.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod]
#endif
        public static void Register()
        {
            MessageRegistry.Register(k_RosMessageName, Deserialize);
        }
    }
}
