#!/usr/bin/env bash
# 生成对应语言的protobuf
protoc --proto_path=./Proto/src --csharp_out=D:\\TestUnity\\VSProjects\\TestFrameSync\\Server\\Network\\Proto ./Proto/src/*.proto
# 这一句是生成类，注册协议号等
python3 gen_proto.py

cp -r D:\\TestUnity\\VSProjects\\TestFrameSync\\Server\\Network D:\\TestUnity\\VSProjects\\TestFrameSync\\Client\\Assets\\Scripts
read -p "Press any key to continue."