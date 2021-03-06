using System;
using System.Collections.Generic;
using System.Linq;

using Qirby.Mathematics;

namespace Qirby.Simulation {
    public struct Instruction {
        public Matrix Operation;
        public int[] Parameters;
    }

    public class State {
        private static Dictionary<int, Matrix> _cachedMatrices = new Dictionary<int, Matrix>();
        private Matrix _identity = null;
        public Matrix Identity {
            get {
                if (_identity == null)
                    _identity = MakeIdentity(NumQubits);
                return _identity;
            }
        }
        public int NumQubits { get; private set; }

        private Matrix _stateVector;
        internal Matrix StateVector => _stateVector;

        public State(int numQubits) {
            NumQubits = numQubits;

            _stateVector = new Matrix((int)Math.Pow(2, numQubits), 1);
            _stateVector.Set(0, 0, 1); // Set all qubits to be in 0 position
        }

        public State(State s) {
            NumQubits = s.NumQubits;
            _stateVector = s.StateVector;
        }

        public State Copy() => new State(this);

        public static Matrix MakeIdentity(int numQubits) {
            if (!_cachedMatrices.ContainsKey(numQubits)) {
                Matrix m = Matrix.I;
                for (int i = 1; i < numQubits; i++) {
                    m = Matrix.TensorProduct(m, Matrix.I);
                }
                _cachedMatrices[numQubits] = m;
                return m;
            } else {
                return _cachedMatrices[numQubits];
            }
        }

        public static Matrix MakeBetterShiftOperator(int dist) { // Dist is signed
            if (dist == 0)
                return MakeIdentity(dist);
            int dim = (int)Math.Pow(2, Math.Abs(dist) + 1);
            var mat = new Complex[dim][];

            for (int r = 0; r < dim; r++) {
                mat[r] = new Complex[dim];
                for (int c = 0; c < dim; c++) {
                    mat[r][c] = 0; // FUCKKKKKK YOU
                }
            }

            if (dist > 0) {
                for (int r = 0; r < dim; r++) {
                    int c = 0;
                    if (r % 2 == 1)
                        c = (dim / 2);
                    mat[r][c + r / 2] = 1;
                }
            } else {
                for (int r = 0; r < dim; r++) {
                    if (r * 2 < dim)
                        mat[r][(r * 2)] = 1;
                    else
                        mat[r][((r - (dim / 2)) * 2) + 1] = 1;
                }
            }
            return new Matrix(mat);
        }

        public Matrix MakeShiftOperator(int current, int target)
            => MakeShiftOperator(NumQubits, current, target);
        public static Matrix MakeShiftOperator(int numQubits, int current, int target) {
            if (current == target)
                return MakeIdentity(numQubits);

            int dist = Math.Abs(target - current); // Dist is not signed
            int dir = (target - current) / dist;

            Matrix s = MakeBetterShiftOperator(dist * dir);
            int lowest = current < target ? current : target;
            Matrix m = lowest == 0 ? s : Matrix.I;
            for (int i = 1; i < numQubits - (dist); i++) { // ye?
                m = Matrix.TensorProduct(m, i == lowest ? s : Matrix.I);
            }

            return m;
        }

        public Matrix MakeOperation(Matrix op, params int[] qubits)
            => MakeOperation(NumQubits, op, qubits);

        public static Matrix MakeOperation(int numQubits, Matrix op, params int[] qubits) {
            // Shift all qubits
            Matrix shift = MakeIdentity(numQubits);
            var shifts = new int[qubits.Length];
            for (int i = 0; i < qubits.Length; i++) {
                shifts[i] = i - qubits[i];
                var sop = MakeShiftOperator(numQubits, i - shifts[i], i);
                if (sop.Columns__ != shift.Columns__)
                    Console.WriteLine("Found it");
                shift = sop * shift;
                for (int j = i + 1; j < qubits.Length; j++) {
                    if (qubits[j] < qubits[i])
                        qubits[j] = qubits[j] + 1;
                }
            }

            Matrix operation = shift;
            if (qubits.Length > 0) {
                Matrix m = op;
                int numQubitsInOp = (int)Math.Log2(op.Columns__);
                for (int i = 1; i <= numQubits - numQubitsInOp; i++) {
                    m = Matrix.TensorProduct(m, Matrix.I);
                }
                operation = m * operation;
            } else {
                operation = op * operation;
            }

            // Shift all qubits back
            Matrix unshift = MakeIdentity(numQubits);
            for (int i = shifts.Length - 1; i >= 0; i--) {
                unshift = MakeShiftOperator(numQubits, i, i - shifts[i]) * unshift;
            }
            operation = unshift * operation;

            return operation;
        }

        public void ApplyOperation(Instruction i) {
            _stateVector = MakeOperation(i.Operation, i.Parameters) * _stateVector;
        }

        public void ApplyOperation(Matrix op, params int[] qubits) {
            _stateVector = MakeOperation(op, qubits) * _stateVector;
        }

        public Matrix CompileInstructionSet(params object[] instructionSet)
            => CompileInstructionSet(NumQubits, instructionSet);
        public static Matrix CompileInstructionSet(int numQubits, params object[] instructionSet) {
            Matrix op = MakeIdentity(numQubits);
            List<int> param = new List<int>();
            List<Instruction> instructions = new List<Instruction>();
            for (int i = 0; i < instructionSet.Length; i++) {
                object a = instructionSet[i];
                if (a is Matrix) {
                    if (i != 0) {
                        instructions.Add(new Instruction { Operation = op, Parameters = param.Count == 0 ? new int[0] : param.ToArray() });
                        param.Clear();
                    }
                    op = a as Matrix;
                } else {
                    param.Add((int)a);
                }
            }
            if (op != null)
                instructions.Add(new Instruction { Operation = op, Parameters = param.Count == 0 ? new int[0] : param.ToArray() });
            
            return CompileInstructionSet(numQubits, instructions.ToArray());
        }

        public Matrix CompileInstructionSet(params Instruction[] instructions)
            => CompileInstructionSet(NumQubits, instructions);
        public static Matrix CompileInstructionSet(int numQubits, params Instruction[] instructions) {
            Matrix op = MakeIdentity(numQubits);
            for (int i = 0; i < instructions.Length; i++) {
                op = MakeOperation(numQubits, instructions[i].Operation, instructions[i].Parameters) * op;
            }
            return op;
        }

        public Dictionary<int[], double> GetProbabilities() {
            var dict = new Dictionary<int[], double>();
            for (int i = 0; i < Math.Pow(2, NumQubits); i++) {
                dict.Add(GetStateRepFromIndex(i), (_stateVector.Get(i, 0) * _stateVector.Get(i, 0)).Magnitude);
            }
            return dict;
        }

        public int[] GetStateRepFromIndex(int a) {
            int[] res = new int[NumQubits];
            for (int i = NumQubits - 1; i >= 0; i--) {
                if (a >= 0x1 << i) {
                    a -= 0x1 << i;
                    res[i] = 1;
                } else {
                    res[i] = 0;
                }
            }
            return res;
        }
    }

    public class Qubit {
        private State _state;
        private int _qubit;

        public Qubit(State state, int qubit) {
            _state = state;
            _qubit = qubit;
        }
    }
}