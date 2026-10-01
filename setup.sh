echo "Provide the path to the The Message From Deep Space";
read -e -p "" path;

if [ ! -f "$path/The Message From Deep Space.exe" ]; then
  echo "Wrong";
  exit -1;
fi

cat > TPFDS.props << EOF
<Project>
  <PropertyGroup>
    <TMFDS_Root>$path</TMFDS_Root>
    <TMFDS_Libs>\$(TMFDS_Root)/The Message From Deep Space_Data/Managed</TMFDS_Libs>
  </PropertyGroup>

  <Target Name="Copy to plugins" AfterTargets="Build">
    <ItemGroup>
      <Compiled Include="\$(TargetDir)/\$(AssemblyName).dll;\$(TargetDir)/\$(AssemblyName).pdb" />
    </ItemGroup>

    <Message Importance="High" Text="Copying output to %GAMEROOT%/BepInEx/plugins" />
    <Copy SourceFiles="@(Compiled)" DestinationFolder="\$(TMFDS_Root)/BepInEx/plugins" />
  </Target>
</Project>
EOF
