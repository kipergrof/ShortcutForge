namespace ShortcutForge.Core.Tests;

internal static class PlistSamples
{
    public const string SampleXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>WFWorkflowClientVersion</key><string>2607.0.2</string>
          <key>WFWorkflowIcon</key>
          <dict>
            <key>WFWorkflowIconGlyphNumber</key><integer>59511</integer>
            <key>WFWorkflowIconStartColor</key><integer>4282601983</integer>
          </dict>
          <key>WFWorkflowImportQuestions</key><array/>
          <key>WFWorkflowInputContentItemClasses</key><array><string>WFStringContentItem</string></array>
          <key>WFWorkflowTypes</key><array><string>NCWidget</string></array>
          <key>WFWorkflowActions</key>
          <array>
            <dict>
              <key>WFWorkflowActionIdentifier</key><string>is.workflow.actions.gettext</string>
              <key>WFWorkflowActionParameters</key>
              <dict>
                <key>UUID</key><string>AAAA-1</string>
                <key>WFTextActionText</key>
                <dict>
                  <key>Value</key>
                  <dict>
                    <key>string</key><string>Hello &#xFFFC;!</string>
                    <key>attachmentsByRange</key>
                    <dict>
                      <key>{6, 1}</key>
                      <dict><key>Type</key><string>ExtensionInput</string></dict>
                    </dict>
                  </dict>
                  <key>WFSerializationType</key><string>WFTextTokenString</string>
                </dict>
              </dict>
            </dict>
            <dict>
              <key>WFWorkflowActionIdentifier</key><string>is.workflow.actions.dictionary</string>
              <key>WFWorkflowActionParameters</key>
              <dict>
                <key>WFItems</key>
                <dict>
                  <key>Value</key>
                  <dict>
                    <key>WFDictionaryFieldValueItems</key>
                    <array>
                      <dict>
                        <key>WFItemType</key><integer>0</integer>
                        <key>WFKey</key>
                        <dict>
                          <key>Value</key><dict><key>string</key><string>name</string><key>attachmentsByRange</key><dict/></dict>
                          <key>WFSerializationType</key><string>WFTextTokenString</string>
                        </dict>
                        <key>WFValue</key>
                        <dict>
                          <key>Value</key><dict><key>string</key><string>Bob</string><key>attachmentsByRange</key><dict/></dict>
                          <key>WFSerializationType</key><string>WFTextTokenString</string>
                        </dict>
                      </dict>
                    </array>
                  </dict>
                  <key>WFSerializationType</key><string>WFDictionaryFieldValue</string>
                </dict>
              </dict>
            </dict>
            <dict>
              <key>WFWorkflowActionIdentifier</key><string>is.workflow.actions.alert</string>
              <key>WFWorkflowActionParameters</key>
              <dict>
                <key>WFAlertActionCancelButtonShown</key><false/>
                <key>WFAlertActionMessage</key>
                <dict>
                  <key>Value</key>
                  <dict>
                    <key>Type</key><string>ActionOutput</string>
                    <key>OutputUUID</key><string>AAAA-1</string>
                    <key>OutputName</key><string>Text</string>
                    <key>Aggrandizements</key>
                    <array>
                      <dict>
                        <key>Type</key><string>WFCoercionVariableAggrandizement</string>
                        <key>CoercionItemClass</key><string>WFStringContentItem</string>
                      </dict>
                    </array>
                  </dict>
                  <key>WFSerializationType</key><string>WFTextTokenAttachment</string>
                </dict>
                <key>SomeNumber</key><real>1.5</real>
              </dict>
            </dict>
          </array>
        </dict>
        </plist>
        """;

}
