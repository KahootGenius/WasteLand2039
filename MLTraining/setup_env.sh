#!/bin/zsh
# Creates the Python environment for ML-Agents Release 21 (mlagents 1.0.0, Unity package
# com.unity.ml-agents 3.0.0-exp.1) on Apple Silicon, without compiling anything:
#   - mlagents-envs pins numpy==1.21.2, which has no cp310 arm64 wheel -> numpy 1.21.6 (same API)
#   - grpcio <= 1.48.2 has no arm64 wheel on PyPI -> conda-forge grpcio 1.48.0
#   - onnx==1.12.0 has no arm64 wheel; torch 2.1's exporter needs onnx -> onnx 1.13.1 (universal2)
#   - onnx 1.13 needs protobuf >= 3.20.2; mlagents declares < 3.20 but its generated gRPC code
#     works on 3.20.x (the break is 4.x; verified by a message round-trip) -> protobuf 3.20.3
#   - tensorboard wants grpcio >= 1.48.2 only for its server -> installed without deps
#   - mlagents imports pkg_resources -> setuptools < 70
# Usage: MLTraining/setup_env.sh        (skips the conda step if the env already exists)
set -euo pipefail
ENV_NAME=mlagents
CONDA=/opt/miniconda3/bin/conda
PREFIX=/opt/miniconda3/envs/$ENV_NAME
PY=$PREFIX/bin/python
HERE="${0:A:h}"

if [[ ! -x $PY ]]; then
  echo "== conda create $ENV_NAME (conda-forge only)"
  $CONDA create -y -n $ENV_NAME --override-channels -c conda-forge \
    python=3.10.12 numpy=1.21.6 grpcio=1.48.0 h5py pillow pyyaml cloudpickle filelock six attrs packaging pip
fi

cat > "$PREFIX/constraints.txt" <<'EOF'
numpy==1.21.6
protobuf==3.20.3
grpcio==1.48.0
EOF

echo "== pip: torch, protobuf, gym stack"
$PY -m pip install --no-cache-dir -c "$PREFIX/constraints.txt" \
  torch==2.1.2 protobuf==3.20.3 onnx==1.13.1 "cattrs>=1.1.0,<1.7" "huggingface_hub>=0.14,<0.25" \
  gym==0.26.2 pettingzoo==1.15.0 "setuptools<70"   # mlagents imports pkg_resources

echo "== pip: tensorboard (writer + viewer, without its grpcio requirement)"
$PY -m pip install --no-cache-dir --no-deps tensorboard==2.14.0 tensorboard-data-server==0.7.2
$PY -m pip install --no-cache-dir -c "$PREFIX/constraints.txt" absl-py markdown werkzeug

echo "== pip: mlagents 1.0.0 (without its numpy/onnx pins)"
$PY -m pip install --no-cache-dir --no-deps mlagents-envs==1.0.0 mlagents==1.0.0

echo "== check (pip check reports the declared numpy/protobuf/grpcio pins; that is expected)"
$PY - <<'EOF'
import os, tempfile, numpy, torch, google.protobuf, grpc, onnx
import mlagents.trainers, mlagents_envs
from torch.utils.tensorboard import SummaryWriter
from mlagents_envs.communicator_objects.unity_message_pb2 import UnityMessageProto
msg = UnityMessageProto(); msg.unity_input.rl_initialization_input.seed = 42
back = UnityMessageProto(); back.ParseFromString(msg.SerializeToString())
assert back.unity_input.rl_initialization_input.seed == 42
path = os.path.join(tempfile.mkdtemp(), "t.onnx")
torch.onnx.export(torch.nn.Linear(4, 2), torch.zeros(1, 4), path, opset_version=9)
onnx.checker.check_model(onnx.load(path))
print("numpy", numpy.__version__, "| torch", torch.__version__, "| protobuf", google.protobuf.__version__,
      "| grpcio", grpc.__version__, "| onnx", onnx.__version__, "| mlagents", mlagents.trainers.__version__,
      "| gRPC messages + ONNX export OK")
EOF
$PREFIX/bin/mlagents-learn --help > /dev/null && echo "mlagents-learn OK"
