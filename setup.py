import os
import shutil

base_dir = os.getcwd()
os.chdir("Addons/qodot-plugin/addons/")
shutil.copytree("qodot/", "../../../Game/addons/qodot/", dirs_exist_ok=True)

os.chdir("../../godot-console/addons/")
shutil.copytree("quentincaffeino/", "../../../Game/addons/quentincaffeino/", dirs_exist_ok=True)