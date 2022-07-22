import os
import subprocess
import shutil
import argparse

parser = argparse.ArgumentParser(description='Export and package project.')
parser.add_argument('--target',
                    help='The build target')
args = parser.parse_args()

target = "release"
scons_export_type = "--export"
if (args.target == "release_debug"):
    target = "release_debug"
    scons_export_type = "--export-debug"

print(target)
base_dir = os.getcwd()

os.chdir("EngineSrc-godot/bin")
subprocess.run(["godot.windows.opt.tools.64.mono.exe", "--path",  "../../Game", scons_export_type, "Windows-CustomFork", "../bin/{}/CyberStealth.exe".format(target)])
shutil.copyfile("mono-2.0-sgen.dll", "../../bin/{}/mono-2.0-sgen.dll".format(target))
shutil.copytree("data.mono.windows.64.release/", "../../bin/{}/data.mono.windows.64.release/".format(target), dirs_exist_ok=True)

# Copy over libqodot files
os.chdir("../../qodot-plugin/libqodot")
subprocess.run(["scons", "p=windows", "target=release".format(target)])
shutil.copyfile("libmap/build/libmap.dll", "../../bin/{}/libmap.dll".format(target))
shutil.copyfile("build/libqodot.dll", "../../bin/{}/libqodot.dll".format(target))

# Copy over trenchbroom files
os.chdir("../../Game/")
shutil.copytree("trenchbroom_data/", "../bin/{}/trenchbroom_data/".format(target), dirs_exist_ok=True)
