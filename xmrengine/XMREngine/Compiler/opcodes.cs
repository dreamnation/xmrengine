// dump opcode table
//   gmcs -out:opcodes.exe opcodes.cs
//   mono opcodes.exe

using System;
using System.Reflection;
using System.Reflection.Emit;

public class Opcodes {
    public static void Main (string[] args)
    {
        FieldInfo[] opcodesFields = typeof (OpCodes).GetFields ();
        foreach (FieldInfo opcodesField in opcodesFields) {
            if (opcodesField.IsStatic && (opcodesField.FieldType == typeof (OpCode))) {
                string name = opcodesField.Name;
                OpCode opcode = (OpCode)opcodesField.GetValue (null);
                /*
                    public OpCodeType OpCodeType {
                    public OperandType OperandType {
                    public FlowControl FlowControl {
                    public StackBehaviour StackBehaviourPop {
                    public StackBehaviour StackBehaviourPush {
                */
                Console.WriteLine (name.PadRight (20) + 
                        opcode.ToString ().PadRight (20) + 
                        opcode.OpCodeType.ToString ().PadRight (12) + 
                        opcode.OperandType.ToString ().PadRight (24) + 
                        opcode.FlowControl.ToString ().PadRight (15) + 
                        opcode.StackBehaviourPop.ToString ().PadRight (20) + 
                        opcode.StackBehaviourPush.ToString ());
            }
        }
    }
}
