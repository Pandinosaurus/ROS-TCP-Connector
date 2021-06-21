//Do not edit! This file was generated by Unity-ROS MessageGeneration.
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

namespace RosMessageTypes.Trajectory
{
    [Serializable]
    public class MultiDOFJointTrajectoryMsg : Message
    {
        public const string k_RosMessageName = "trajectory_msgs/MultiDOFJointTrajectory";

        //  The header is used to specify the coordinate frame and the reference time for the trajectory durations
        public Std.HeaderMsg header;
        //  A representation of a multi-dof joint trajectory (each point is a transformation)
        //  Each point along the trajectory will include an array of positions/velocities/accelerations
        //  that has the same length as the array of joint names, and has the same order of joints as 
        //  the joint names array.
        public string[] joint_names;
        public MultiDOFJointTrajectoryPointMsg[] points;

        public MultiDOFJointTrajectoryMsg()
        {
            this.header = new Std.HeaderMsg();
            this.joint_names = new string[0];
            this.points = new MultiDOFJointTrajectoryPointMsg[0];
        }

        public MultiDOFJointTrajectoryMsg(Std.HeaderMsg header, string[] joint_names, MultiDOFJointTrajectoryPointMsg[] points)
        {
            this.header = header;
            this.joint_names = joint_names;
            this.points = points;
        }

        public static MultiDOFJointTrajectoryMsg Deserialize(MessageDeserializer deserializer) => new MultiDOFJointTrajectoryMsg(deserializer);

        private MultiDOFJointTrajectoryMsg(MessageDeserializer deserializer)
        {
            this.header = Std.HeaderMsg.Deserialize(deserializer);
            deserializer.Read(out this.joint_names, deserializer.ReadLength());
            deserializer.Read(out this.points, MultiDOFJointTrajectoryPointMsg.Deserialize, deserializer.ReadLength());
        }

        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(this.header);
            serializer.WriteLength(this.joint_names);
            serializer.Write(this.joint_names);
            serializer.WriteLength(this.points);
            serializer.Write(this.points);
        }

        public override string ToString()
        {
            return "MultiDOFJointTrajectoryMsg: " +
            "\nheader: " + header.ToString() +
            "\njoint_names: " + System.String.Join(", ", joint_names.ToList()) +
            "\npoints: " + System.String.Join(", ", points.ToList());
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
