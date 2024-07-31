#!/bin/bash

# Detect the operating system
os_type=$(uname)

# Execute the corresponding script based on the operating system
case "$os_type" in
  Linux)
    echo $(./find-elf.sh "$1")
    ;;
  Darwin)
    echo $(./find-macho.sh "$1")
    ;;
  *)
    echo "Unsupported OS: $os_type"
    exit 1
    ;;
esac
