import os
import subprocess
import shutil
import argparse

parser = argparse.ArgumentParser(description='Export and package project.')
parser.add_argument('--target',
                    help='The build target')
args = parser.parse_args()

target = "release"
if (args.target == "release_debug"):
    target = "release_debug"

print(target)
base_dir = os.getcwd()

os.chdir("EngineSrc-godot/bin")
subprocess.run(["godot.windows.opt.tools.64.mono.exe", "--path",  "../../Game", "--export", "Windows-CustomFork", "../bin/{}/CyberStealth.exe".format(target)])
shutil.copyfile("mono-2.0-sgen.dll", "../../bin/{}/mono-2.0-sgen.dll".format(target))
shutil.copytree("data.mono.windows.64.release/", "../../bin/{}/data.mono.windows.64.release/".format(target), dirs_exist_ok=True)