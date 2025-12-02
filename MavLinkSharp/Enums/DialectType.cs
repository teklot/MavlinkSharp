namespace MavLinkSharp.Enums
{
    /// <summary>
    /// Represents the MAVLink dialects included in the project.
    /// </summary>
    public enum DialectType
    {
        /// <summary>
        /// The 'all.xml' dialect, which includes all other dialects.
        /// </summary>
        All,
        /// <summary>
        /// The 'ardupilotmega.xml' dialect.
        /// </summary>
        Ardupilotmega,
        /// <summary>
        /// The 'ASLUAV.xml' dialect.
        /// </summary>
        ASLUAV,
        /// <summary>
        /// The 'AVSSUAS.xml' dialect.
        /// </summary>
        AVSSUAS,
        /// <summary>
        /// The 'common.xml' dialect, containing standard MAVLink messages.
        /// </summary>
        Common,
        /// <summary>
        /// The 'cubepilot.xml' dialect.
        /// </summary>
        Cubepilot,
        /// <summary>
        /// The 'development.xml' dialect.
        /// </summary>
        Development,
        /// <summary>
        /// The 'icarous.xml' dialect.
        /// </summary>
        Icarous,
        /// <summary>
        /// The 'matrixpilot.xml' dialect.
        /// </summary>
        Matrixpilot,
        /// <summary>
        /// The 'minimal.xml' dialect.
        /// </summary>
        Minimal,
        /// <summary>
        /// The 'paparazzi.xml' dialect.
        /// </summary>
        Paparazzi,
        /// <summary>
        /// The 'python_array_test.xml' dialect.
        /// </summary>
        PythonArrayTest,
        /// <summary>
        /// The 'standard.xml' dialect.
        /// </summary>
        Standard,
        /// <summary>
        /// The 'storm32.xml' dialect.
        /// </summary>
        Storm32,
        /// <summary>
        /// The 'test.xml' dialect.
        /// </summary>
        Test,
        /// <summary>
        /// The 'ualberta.xml' dialect.
        /// </summary>
        Ualberta,
        /// <summary>
        /// The 'uAvionix.xml' dialect.
        /// </summary>
        UAvionix
    }
}