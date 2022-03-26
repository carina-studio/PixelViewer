using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CarinaStudio;
using CarinaStudio.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace Carina.PixelViewer.Controls;

/// <summary>
/// CIE Chromaticity diagram.
/// </summary>
class CieChromaticityDiagram : Control, IStyleable
{
    /// <summary>
    /// Property of <see cref="AxisBrush"/>.
    /// </summary>
    public static readonly AvaloniaProperty<IBrush?> AxisBrushProperty = AvaloniaProperty.Register<CieChromaticityDiagram, IBrush?>(nameof(AxisBrush), null);
    /// <summary>
    /// Property of <see cref="DiagramBorderBrush"/>.
    /// </summary>
    public static readonly AvaloniaProperty<IBrush?> DiagramBorderBrushProperty = AvaloniaProperty.Register<CieChromaticityDiagram, IBrush?>(nameof(DiagramBorderBrush), null);
    /// <summary>
    /// Property of <see cref="GridBrush"/>.
    /// </summary>
    public static readonly AvaloniaProperty<IBrush?> GridBrushProperty = AvaloniaProperty.Register<CieChromaticityDiagram, IBrush?>(nameof(GridBrush), null);
    /// <summary>
    /// Property of <see cref="FontSize"/>.
    /// </summary>
    public static readonly AvaloniaProperty<double> FontSizeProperty = AvaloniaProperty.Register<CieChromaticityDiagram, double>(nameof(FontSize), 10);


    // Constants.
    const double MaxCoordinateX = 0.8;
    const double MaxCoordinateY = 0.9;


    // Static fields.
    static readonly (Color, double, double)[] ColorCoordinates = new (Color, double, double)[] {
        (Color.FromArgb(0xff, 0xff, 0x4c, 0x4c), 0.72329, 0.27671), // 700 nm
        (Color.FromArgb(0xff, 0x4c, 0x4c, 0xff), 0.15225, 0.02008), // 435.8 nm
        (Color.FromArgb(0xff, 0x4c, 0xff, 0x4c), 0.28489, 0.71108), // 546.1 nm
    };
    static readonly (int, double, double)[] XYCoordinates = new (int, double, double)[] {
        // With 2-degree observer (http://www.cvrl.org/)
        (390, 0.16638, 0.01830),
        (391, 0.16635, 0.01846),
        (392, 0.16629, 0.01858),
        (393, 0.16620, 0.01867),
        (394, 0.16609, 0.01872),
        (395, 0.16595, 0.01874),
        (396, 0.16579, 0.01872),
        (397, 0.16561, 0.01867),
        (398, 0.16542, 0.01857),
        (399, 0.16521, 0.01844),
        (400, 0.16499, 0.01827),
        (401, 0.16477, 0.01807),
        (402, 0.16455, 0.01784),
        (403, 0.16433, 0.01761),
        (404, 0.16412, 0.01738),
        (405, 0.16393, 0.01718),
        (406, 0.16376, 0.01702),
        (407, 0.16359, 0.01688),
        (408, 0.16341, 0.01676),
        (409, 0.16320, 0.01664),
        (410, 0.16296, 0.01653),
        (411, 0.16266, 0.01640),
        (412, 0.16233, 0.01627),
        (413, 0.16198, 0.01614),
        (414, 0.16162, 0.01603),
        (415, 0.16126, 0.01594),
        (416, 0.16093, 0.01587),
        (417, 0.16060, 0.01583),
        (418, 0.16028, 0.01582),
        (419, 0.15994, 0.01584),
        (420, 0.15958, 0.01589),
        (421, 0.15920, 0.01597),
        (422, 0.15879, 0.01608),
        (423, 0.15836, 0.01622),
        (424, 0.15793, 0.01637),
        (425, 0.15750, 0.01653),
        (426, 0.15709, 0.01671),
        (427, 0.15669, 0.01690),
        (428, 0.15628, 0.01712),
        (429, 0.15586, 0.01738),
        (430, 0.15540, 0.01767),
        (431, 0.15491, 0.01802),
        (432, 0.15439, 0.01841),
        (433, 0.15384, 0.01883),
        (434, 0.15329, 0.01926),
        (435, 0.15276, 0.01968),
        (436, 0.15225, 0.02008),
        (437, 0.15178, 0.02047),
        (438, 0.15131, 0.02086),
        (439, 0.15085, 0.02128),
        (440, 0.15036, 0.02173),
        (441, 0.14985, 0.02225),
        (442, 0.14929, 0.02282),
        (443, 0.14871, 0.02343),
        (444, 0.14811, 0.02409),
        (445, 0.14749, 0.02478),
        (446, 0.14687, 0.02550),
        (447, 0.14624, 0.02625),
        (448, 0.14560, 0.02706),
        (449, 0.14493, 0.02795),
        (450, 0.14423, 0.02895),
        (451, 0.14349, 0.03007),
        (452, 0.14271, 0.03133),
        (453, 0.14186, 0.03271),
        (454, 0.14095, 0.03422),
        (455, 0.13997, 0.03584),
        (456, 0.13890, 0.03759),
        (457, 0.13775, 0.03945),
        (458, 0.13653, 0.04145),
        (459, 0.13525, 0.04359),
        (460, 0.13392, 0.04588),
        (461, 0.13254, 0.04834),
        (462, 0.13112, 0.05098),
        (463, 0.12964, 0.05384),
        (464, 0.12807, 0.05695),
        (465, 0.12638, 0.06036),
        (466, 0.12456, 0.06410),
        (467, 0.12259, 0.06822),
        (468, 0.12046, 0.07275),
        (469, 0.11818, 0.07773),
        (470, 0.11574, 0.08320),
        (471, 0.11313, 0.08922),
        (472, 0.11034, 0.09582),
        (473, 0.10736, 0.10307),
        (474, 0.10418, 0.11103),
        (475, 0.10078, 0.11975),
        (476, 0.09715, 0.12930),
        (477, 0.09329, 0.13971),
        (478, 0.08923, 0.15097),
        (479, 0.08497, 0.16308),
        (480, 0.08055, 0.17601),
        (481, 0.07599, 0.18973),
        (482, 0.07130, 0.20429),
        (483, 0.06649, 0.21974),
        (484, 0.06155, 0.23615),
        (485, 0.05651, 0.25359),
        (486, 0.05138, 0.27212),
        (487, 0.04622, 0.29169),
        (488, 0.04108, 0.31224),
        (489, 0.03604, 0.33365),
        (490, 0.03117, 0.35580),
        (491, 0.02654, 0.37853),
        (492, 0.02222, 0.40175),
        (493, 0.01827, 0.42531),
        (494, 0.01477, 0.44909),
        (495, 0.01178, 0.47294),
        (496, 0.00933, 0.49673),
        (497, 0.00741, 0.52049),
        (498, 0.00596, 0.54424),
        (499, 0.00490, 0.56805),
        (500, 0.00418, 0.59194),
        (501, 0.00374, 0.61593),
        (502, 0.00364, 0.63984),
        (503, 0.00393, 0.66346),
        (504, 0.00471, 0.68656),
        (505, 0.00605, 0.70890),
        (506, 0.00802, 0.73018),
        (507, 0.01073, 0.74993),
        (508, 0.01424, 0.76771),
        (509, 0.01860, 0.78319),
        (510, 0.02382, 0.79618),
        (511, 0.02983, 0.80664),
        (512, 0.03645, 0.81498),
        (513, 0.04345, 0.82161),
        (514, 0.05062, 0.82692),
        (515, 0.05780, 0.83125),
        (516, 0.06485, 0.83484),
        (517, 0.07183, 0.83765),
        (518, 0.07882, 0.83959),
        (519, 0.08593, 0.84060),
        (520, 0.09322, 0.84063),
        (521, 0.10076, 0.83968),
        (522, 0.10849, 0.83786),
        (523, 0.11636, 0.83533),
        (524, 0.12431, 0.83221),
        (525, 0.13227, 0.82861),
        (526, 0.14020, 0.82463),
        (527, 0.14806, 0.82034),
        (528, 0.15582, 0.81580),
        (529, 0.16344, 0.81106),
        (530, 0.17090, 0.80617),
        (531, 0.17820, 0.80119),
        (532, 0.18536, 0.79609),
        (533, 0.19247, 0.79084),
        (534, 0.19958, 0.78542),
        (535, 0.20674, 0.77980),
        (536, 0.21399, 0.77394),
        (537, 0.22130, 0.76790),
        (538, 0.22862, 0.76172),
        (539, 0.23592, 0.75544),
        (540, 0.24316, 0.74912),
        (541, 0.25030, 0.74278),
        (542, 0.25736, 0.73644),
        (543, 0.26434, 0.73010),
        (544, 0.27125, 0.72376),
        (545, 0.27809, 0.71742),
        (546, 0.28489, 0.71108),
        (547, 0.29164, 0.70474),
        (548, 0.29834, 0.69841),
        (549, 0.30499, 0.69208),
        (550, 0.31161, 0.68576),
        (551, 0.31821, 0.67944),
        (552, 0.32481, 0.67307),
        (553, 0.33148, 0.66663),
        (554, 0.33825, 0.66006),
        (555, 0.34516, 0.65332),
        (556, 0.35222, 0.64642),
        (557, 0.35937, 0.63941),
        (558, 0.36653, 0.63237),
        (559, 0.37363, 0.62538),
        (560, 0.38061, 0.61851),
        (561, 0.38743, 0.61177),
        (562, 0.39416, 0.60512),
        (563, 0.40087, 0.59849),
        (564, 0.40763, 0.59179),
        (565, 0.41450, 0.58498),
        (566, 0.42152, 0.57801),
        (567, 0.42864, 0.57094),
        (568, 0.43579, 0.56383),
        (569, 0.44293, 0.55672),
        (570, 0.45001, 0.54968),
        (571, 0.45698, 0.54274),
        (572, 0.46388, 0.53587),
        (573, 0.47071, 0.52906),
        (574, 0.47751, 0.52229),
        (575, 0.48429, 0.51553),
        (576, 0.49105, 0.50878),
        (577, 0.49782, 0.50203),
        (578, 0.50458, 0.49529),
        (579, 0.51133, 0.48854),
        (580, 0.51808, 0.48181),
        (581, 0.52481, 0.47509),
        (582, 0.53145, 0.46846),
        (583, 0.53795, 0.46197),
        (584, 0.54425, 0.45567),
        (585, 0.55031, 0.44962),
        (586, 0.55610, 0.44384),
        (587, 0.56167, 0.43828),
        (588, 0.56707, 0.43288),
        (589, 0.57236, 0.42759),
        (590, 0.57757, 0.42238),
        (591, 0.58273, 0.41723),
        (592, 0.58783, 0.41214),
        (593, 0.59283, 0.40714),
        (594, 0.59772, 0.40225),
        (595, 0.60249, 0.39748),
        (596, 0.60712, 0.39285),
        (597, 0.61163, 0.38834),
        (598, 0.61604, 0.38394),
        (599, 0.62034, 0.37963),
        (600, 0.62457, 0.37541),
        (601, 0.62871, 0.37127),
        (602, 0.63276, 0.36723),
        (603, 0.63668, 0.36330),
        (604, 0.64046, 0.35952),
        (605, 0.64409, 0.35589),
        (606, 0.64756, 0.35243),
        (607, 0.65088, 0.34911),
        (608, 0.65405, 0.34594),
        (609, 0.65708, 0.34291),
        (610, 0.66000, 0.33999),
        (611, 0.66280, 0.33719),
        (612, 0.66548, 0.33451),
        (613, 0.66807, 0.33193),
        (614, 0.67055, 0.32945),
        (615, 0.67293, 0.32706),
        (616, 0.67523, 0.32477),
        (617, 0.67744, 0.32256),
        (618, 0.67958, 0.32042),
        (619, 0.68166, 0.31834),
        (620, 0.68369, 0.31631),
        (621, 0.68567, 0.31433),
        (622, 0.68758, 0.31242),
        (623, 0.68940, 0.31060),
        (624, 0.69111, 0.30889),
        (625, 0.69269, 0.30731),
        (626, 0.69415, 0.30585),
        (627, 0.69549, 0.30451),
        (628, 0.69675, 0.30325),
        (629, 0.69793, 0.30207),
        (630, 0.69907, 0.30093),
        (631, 0.70017, 0.29983),
        (632, 0.70124, 0.29876),
        (633, 0.70228, 0.29772),
        (634, 0.70329, 0.29671),
        (635, 0.70426, 0.29574),
        (636, 0.70520, 0.29480),
        (637, 0.70612, 0.29388),
        (638, 0.70703, 0.29297),
        (639, 0.70795, 0.29205),
        (640, 0.70887, 0.29113),
        (641, 0.70980, 0.29020),
        (642, 0.71071, 0.28929),
        (643, 0.71157, 0.28843),
        (644, 0.71236, 0.28764),
        (645, 0.71304, 0.28696),
        (646, 0.71361, 0.28639),
        (647, 0.71410, 0.28590),
        (648, 0.71452, 0.28548),
        (649, 0.71490, 0.28510),
        (650, 0.71528, 0.28472),
        (651, 0.71566, 0.28434),
        (652, 0.71605, 0.28395),
        (653, 0.71645, 0.28355),
        (654, 0.71685, 0.28315),
        (655, 0.71725, 0.28275),
        (656, 0.71764, 0.28236),
        (657, 0.71802, 0.28198),
        (658, 0.71840, 0.28160),
        (659, 0.71876, 0.28124),
        (660, 0.71912, 0.28088),
        (661, 0.71946, 0.28054),
        (662, 0.71978, 0.28022),
        (663, 0.72009, 0.27991),
        (664, 0.72037, 0.27963),
        (665, 0.72062, 0.27938),
        (666, 0.72084, 0.27916),
        (667, 0.72104, 0.27896),
        (668, 0.72122, 0.27878),
        (669, 0.72139, 0.27861),
        (670, 0.72154, 0.27846),
        (671, 0.72169, 0.27831),
        (672, 0.72182, 0.27818),
        (673, 0.72195, 0.27805),
        (674, 0.72208, 0.27792),
        (675, 0.72219, 0.27781),
        (676, 0.72230, 0.27770),
        (677, 0.72239, 0.27761),
        (678, 0.72248, 0.27752),
        (679, 0.72257, 0.27743),
        (680, 0.72265, 0.27735),
        (681, 0.72272, 0.27728),
        (682, 0.72279, 0.27721),
        (683, 0.72285, 0.27715),
        (684, 0.72291, 0.27709),
        (685, 0.72296, 0.27704),
        (686, 0.72300, 0.27700),
        (687, 0.72304, 0.27696),
        (688, 0.72308, 0.27692),
        (689, 0.72311, 0.27689),
        (690, 0.72314, 0.27686),
        (691, 0.72317, 0.27683),
        (692, 0.72320, 0.27680),
        (693, 0.72323, 0.27677),
        (694, 0.72325, 0.27675),
        (695, 0.72327, 0.27673),
        (696, 0.72328, 0.27672),
        (697, 0.72329, 0.27671),
        (698, 0.72329, 0.27671),
        (699, 0.72329, 0.27671),
        (700, 0.72329, 0.27671),
        (701, 0.72329, 0.27671),
        (702, 0.72329, 0.27671),
        (703, 0.72329, 0.27671),
        (704, 0.72329, 0.27671),
        (705, 0.72329, 0.27671),
        (706, 0.72329, 0.27671),
        (707, 0.72328, 0.27672),
        (708, 0.72327, 0.27673),
        (709, 0.72325, 0.27675),
        (710, 0.72323, 0.27677),
        (711, 0.72320, 0.27680),
        (712, 0.72318, 0.27682),
        (713, 0.72314, 0.27686),
        (714, 0.72311, 0.27689),
        (715, 0.72308, 0.27692),
        (716, 0.72305, 0.27695),
        (717, 0.72301, 0.27699),
        (718, 0.72298, 0.27702),
        (719, 0.72295, 0.27705),
        (720, 0.72292, 0.27708),
        (721, 0.72288, 0.27712),
        (722, 0.72284, 0.27716),
        (723, 0.72280, 0.27720),
        (724, 0.72276, 0.27724),
        (725, 0.72272, 0.27728),
        (726, 0.72268, 0.27732),
        (727, 0.72264, 0.27736),
        (728, 0.72259, 0.27741),
        (729, 0.72255, 0.27745),
        (730, 0.72251, 0.27749),
        (731, 0.72246, 0.27754),
        (732, 0.72242, 0.27758),
        (733, 0.72237, 0.27763),
        (734, 0.72233, 0.27767),
        (735, 0.72228, 0.27772),
        (736, 0.72222, 0.27778),
        (737, 0.72217, 0.27783),
        (738, 0.72211, 0.27789),
        (739, 0.72204, 0.27796),
        (740, 0.72198, 0.27802),
        (741, 0.72191, 0.27809),
        (742, 0.72184, 0.27816),
        (743, 0.72177, 0.27823),
        (744, 0.72169, 0.27831),
        (745, 0.72162, 0.27838),
        (746, 0.72155, 0.27845),
        (747, 0.72148, 0.27852),
        (748, 0.72141, 0.27859),
        (749, 0.72134, 0.27866),
        (750, 0.72127, 0.27873),
        (751, 0.72120, 0.27880),
        (752, 0.72112, 0.27888),
        (753, 0.72105, 0.27895),
        (754, 0.72098, 0.27902),
        (755, 0.72091, 0.27909),
        (756, 0.72083, 0.27917),
        (757, 0.72075, 0.27925),
        (758, 0.72068, 0.27932),
        (759, 0.72060, 0.27940),
        (760, 0.72052, 0.27948),
        (761, 0.72044, 0.27956),
        (762, 0.72037, 0.27963),
        (763, 0.72030, 0.27970),
        (764, 0.72022, 0.27978),
        (765, 0.72015, 0.27985),
        (766, 0.72008, 0.27992),
        (767, 0.72001, 0.27999),
        (768, 0.71994, 0.28006),
        (769, 0.71987, 0.28013),
        (770, 0.71980, 0.28020),
        (771, 0.71973, 0.28027),
        (772, 0.71966, 0.28034),
        (773, 0.71958, 0.28042),
        (774, 0.71951, 0.28049),
        (775, 0.71943, 0.28057),
        (776, 0.71936, 0.28064),
        (777, 0.71928, 0.28072),
        (778, 0.71920, 0.28080),
        (779, 0.71912, 0.28088),
        (780, 0.71904, 0.28096),
        (781, 0.71895, 0.28105),
        (782, 0.71887, 0.28113),
        (783, 0.71879, 0.28121),
        (784, 0.71870, 0.28130),
        (785, 0.71862, 0.28138),
        (786, 0.71853, 0.28147),
        (787, 0.71845, 0.28155),
        (788, 0.71836, 0.28164),
        (789, 0.71828, 0.28172),
        (790, 0.71819, 0.28181),
        (791, 0.71810, 0.28190),
        (792, 0.71802, 0.28198),
        (793, 0.71793, 0.28207),
        (794, 0.71783, 0.28217),
        (795, 0.71774, 0.28226),
        (796, 0.71764, 0.28236),
        (797, 0.71753, 0.28247),
        (798, 0.71743, 0.28257),
        (799, 0.71732, 0.28268),
        (800, 0.71721, 0.28279),
        (801, 0.71710, 0.28290),
        (802, 0.71699, 0.28301),
        (803, 0.71688, 0.28312),
        (804, 0.71677, 0.28323),
        (805, 0.71666, 0.28334),
        (806, 0.71655, 0.28345),
        (807, 0.71644, 0.28356),
        (808, 0.71633, 0.28367),
        (809, 0.71622, 0.28378),
        (810, 0.71611, 0.28389),
        (811, 0.71599, 0.28401),
        (812, 0.71588, 0.28412),
        (813, 0.71577, 0.28423),
        (814, 0.71566, 0.28434),
        (815, 0.71555, 0.28445),
        (816, 0.71544, 0.28456),
        (817, 0.71533, 0.28467),
        (818, 0.71522, 0.28478),
        (819, 0.71512, 0.28488),
        (820, 0.71502, 0.28498),
        (821, 0.71492, 0.28508),
        (822, 0.71482, 0.28518),
        (823, 0.71473, 0.28527),
        (824, 0.71464, 0.28536),
        (825, 0.71455, 0.28545),
        (826, 0.71447, 0.28553),
        (827, 0.71439, 0.28561),
        (828, 0.71431, 0.28569),
        (829, 0.71424, 0.28576),
        (830, 0.71417, 0.28583),
    };
    static readonly (double, double) WhitePointXY = (0.31271, 0.32902); // D65 with 2-degree observer


    // Fields.
    readonly List<CieChromaticity> attachedChromaticities = new();
    readonly List<CieChromaticityGamut> attachedChromaticityGamuts = new();
    Pen? axisPen;
    readonly ObservableList<CieChromaticity> chromaticities = new();
    readonly ObservableList<CieChromaticityGamut> chromaticityGamuts = new();
    StreamGeometry? diagramGeometry;
    IBrush? diagramBrush;
    IBrush? diagramOverlayBrush;
    Pen? diagramPen;
    Pen? gridPen;


    // Static initializer.
    static CieChromaticityDiagram()
    {
        AffectsRender<CieChromaticityDiagram>(
            AxisBrushProperty, 
            DiagramBorderBrushProperty,
            GridBrushProperty,
            FontSizeProperty
        );
    }


    /// <summary>
    /// Initialize new <see cref="CieChromaticityDiagram"/> instance.
    /// </summary>
    public CieChromaticityDiagram()
    {
        this.chromaticities.CollectionChanged += this.OnChromaticitiesChanged;
        this.chromaticityGamuts.CollectionChanged += this.OnChromaticityGamutsChanged;
    }


    // Calculate angle from point to point.
    double AngleToControlCoordinate(Point src, Point dest)
    {
        if (Math.Abs(dest.X - src.X) < 0.001)
            return dest.Y <= src.Y ? 0 : 180;
        if (Math.Abs(dest.Y - src.Y) < 0.001)
            return dest.X >= src.X ? 90 : 270;
        var x = (dest.X - src.X);
        var y = (dest.Y - src.Y);
        if (x >= 0)
        {
            return y >= 0
                ? 180 - Math.Atan(x / y) / Math.PI * 180
                : Math.Atan(x / -y) / Math.PI * 180;
        }
        else
        {
            return y >= 0
                ? 180 + Math.Atan(-x / y) / Math.PI * 180
                : 360 - Math.Atan(x / y) / Math.PI * 180;
        }
    }


    /// <summary>
    /// Get or set brush to draw axises.
    /// </summary>
    public IBrush? AxisBrush
    {
        get => this.GetValue<IBrush?>(AxisBrushProperty);
        set => this.SetValue<IBrush?>(AxisBrushProperty, value);
    }


    /// <summary>
    /// Get list of <see cref="CieChromaticity"/> to be shown in diagram.
    /// </summary>
    public IList<CieChromaticity> Chromaticities { get => this.chromaticities; }


    /// <summary>
    /// Get list of <see cref="CieChromaticityGamut"/> to be shown in diagram.
    /// </summary>
    public IList<CieChromaticityGamut> ChromaticityGamuts { get => this.chromaticityGamuts; }


    /// <summary>
    /// Get or set brush to draw border of diagram.
    /// </summary>
    public IBrush? DiagramBorderBrush
    {
        get => this.GetValue<IBrush?>(DiagramBorderBrushProperty);
        set => this.SetValue<IBrush?>(DiagramBorderBrushProperty, value);
    }


    /// <summary>
    /// Get or set font size.
    /// </summary>
    public double FontSize
    {
        get => this.GetValue<double>(FontSizeProperty);
        set => this.SetValue<double>(FontSizeProperty, value);
    }


    /// <summary>
    /// Get or set brush to draw grid.
    /// </summary>
    public IBrush? GridBrush
    {
        get => this.GetValue<IBrush?>(GridBrushProperty);
        set => this.SetValue<IBrush?>(GridBrushProperty, value);
    }


    // Called when list of chromaticity changed.
    void OnChromaticitiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                {
                    var chromaticities = e.NewItems.AsNonNull().Cast<CieChromaticity>();
                    foreach (var chromaticity in chromaticities)
                        chromaticity.PropertyChanged += this.OnChromaticityPropertyChanged;
                    this.attachedChromaticities.InsertRange(e.NewStartingIndex, chromaticities);
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                {
                    var chromaticities = e.OldItems.AsNonNull().Cast<CieChromaticity>();
                    foreach (var chromaticity in chromaticities)
                        chromaticity.PropertyChanged -= this.OnChromaticityPropertyChanged;
                    this.attachedChromaticities.RemoveRange(e.OldStartingIndex, e.OldItems?.Count ?? 0);
                }
                break;
            case NotifyCollectionChangedAction.Replace:
                {
                    var chromaticities = e.OldItems.AsNonNull().Cast<CieChromaticity>();
                    foreach (var chromaticity in chromaticities)
                        chromaticity.PropertyChanged -= this.OnChromaticityPropertyChanged;
                    this.attachedChromaticities.RemoveRange(e.OldStartingIndex, e.OldItems?.Count ?? 0);
                    chromaticities = e.NewItems.AsNonNull().Cast<CieChromaticity>();
                    foreach (var chromaticity in chromaticities)
                        chromaticity.PropertyChanged += this.OnChromaticityPropertyChanged;
                    this.attachedChromaticities.InsertRange(e.NewStartingIndex, chromaticities);
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                foreach (var chromaticity in this.attachedChromaticities)
                    chromaticity.PropertyChanged -= this.OnChromaticityPropertyChanged;
                this.attachedChromaticities.Clear();
                foreach (var chromaticity in this.chromaticities)
                    chromaticity.PropertyChanged += this.OnChromaticityPropertyChanged;
                this.attachedChromaticities.AddRange(this.chromaticities);
                break;
            default:
                return;
        }
        this.InvalidateVisual();
    }


    // Called when property of chromaticity gamut changed.
    void OnChromaticityGamutPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) =>
        this.InvalidateVisual();


    // Called when list of chromaticity gamuts changed.
    void OnChromaticityGamutsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                {
                    var colorGamuts = e.NewItems.AsNonNull().Cast<CieChromaticityGamut>();
                    foreach (var colorGamut in colorGamuts)
                        colorGamut.PropertyChanged += this.OnChromaticityGamutPropertyChanged;
                    this.attachedChromaticityGamuts.InsertRange(e.NewStartingIndex, colorGamuts);
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                {
                    var colorGamuts = e.OldItems.AsNonNull().Cast<CieChromaticityGamut>();
                    foreach (var colorGamut in colorGamuts)
                        colorGamut.PropertyChanged -= this.OnChromaticityGamutPropertyChanged;
                    this.attachedChromaticityGamuts.RemoveRange(e.OldStartingIndex, e.OldItems?.Count ?? 0);
                }
                break;
            case NotifyCollectionChangedAction.Replace:
                {
                    var colorGamuts = e.OldItems.AsNonNull().Cast<CieChromaticityGamut>();
                    foreach (var colorGamut in colorGamuts)
                        colorGamut.PropertyChanged -= this.OnChromaticityGamutPropertyChanged;
                    this.attachedChromaticityGamuts.RemoveRange(e.OldStartingIndex, e.OldItems?.Count ?? 0);
                    colorGamuts = e.NewItems.AsNonNull().Cast<CieChromaticityGamut>();
                    foreach (var colorGamut in colorGamuts)
                        colorGamut.PropertyChanged += this.OnChromaticityGamutPropertyChanged;
                    this.attachedChromaticityGamuts.InsertRange(e.NewStartingIndex, colorGamuts);
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                foreach (var colorGamut in this.attachedChromaticityGamuts)
                    colorGamut.PropertyChanged -= this.OnChromaticityGamutPropertyChanged;
                this.attachedChromaticityGamuts.Clear();
                foreach (var colorGamut in this.chromaticityGamuts)
                    colorGamut.PropertyChanged += this.OnChromaticityGamutPropertyChanged;
                this.attachedChromaticityGamuts.AddRange(this.chromaticityGamuts);
                break;
            default:
                return;
        }
        this.InvalidateVisual();
    }


    // Called when property of chromaticity changed.
    void OnChromaticityPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) =>
        this.InvalidateVisual();


    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        // get state
        var bounds = this.Bounds;
        var width = bounds.Width;
        var height = bounds.Height;

        // prepare path
        if (this.diagramGeometry == null)
        {
            this.diagramGeometry = new StreamGeometry();
            using var geometryContext = this.diagramGeometry.Open();
            var coordCount = XYCoordinates.Length;
            geometryContext.BeginFigure(this.XYToControlCoordinate(width, height, XYCoordinates[0].Item2, XYCoordinates[0].Item3), true);
            for (var i = 1; i < coordCount; ++i)
                geometryContext.LineTo(this.XYToControlCoordinate(width, height, XYCoordinates[i].Item2, XYCoordinates[i].Item3));
            geometryContext.EndFigure(false);
        }

        // prepare pen and brush
        if (this.axisPen == null)
        {
            var brush = this.AxisBrush;
            if (brush != null)
                this.axisPen = new Pen(brush, 2);
        }
        if (this.diagramPen == null)
        {
            var brush = this.DiagramBorderBrush;
            if (brush != null)
                this.diagramPen = new Pen(brush);
        }
        if (this.diagramBrush == null)
        {
            this.diagramBrush = new ConicGradientBrush().Also(it =>
            {
                var wp = this.XYToControlCoordinate(width, height, WhitePointXY.Item1, WhitePointXY.Item2);
                var firstColorPoint = this.XYToControlCoordinate(width, height, ColorCoordinates[0].Item2, ColorCoordinates[0].Item3);
                it.Angle = this.AngleToControlCoordinate(wp, firstColorPoint);
                it.Center = new RelativePoint(wp, RelativeUnit.Absolute);
                foreach (var coord in ColorCoordinates)
                {
                    var colorPoint = this.XYToControlCoordinate(width, height, coord.Item2, coord.Item3);
                    var angle = this.AngleToControlCoordinate(wp, colorPoint);
                    it.GradientStops.Add(new GradientStop(coord.Item1, (angle - it.Angle) / 360.0));
                }
                it.GradientStops.Add(new GradientStop(ColorCoordinates[0].Item1, 1.0));
            });
        }
        if (this.diagramOverlayBrush == null)
        {
            this.diagramOverlayBrush = new RadialGradientBrush().Also(it =>
            {
                var wp = this.XYToControlCoordinate(width, height, WhitePointXY.Item1, WhitePointXY.Item2);
                it.Center = new RelativePoint(wp, RelativeUnit.Absolute);
                it.GradientOrigin = new RelativePoint(wp, RelativeUnit.Absolute);
                it.GradientStops.Add(new GradientStop(Color.FromArgb(200, 255, 255, 255), 0.0));
                it.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
                it.Radius = 0.4;
            });
        }
        if (this.gridPen == null)
        {
            var brush = this.GridBrush;
            if (brush != null)
                this.gridPen = new Pen(brush);
        }

        // draw axises
        context.DrawLine(this.axisPen, new Point(0, 0), new Point(0, height));
        context.DrawLine(this.axisPen, new Point(0, height), new Point(width, height));

        // draw diagram
        context.DrawGeometry(this.diagramBrush, this.diagramPen, this.diagramGeometry);
        context.DrawGeometry(this.diagramOverlayBrush, null, this.diagramGeometry);

        // draw chromaticity gamuts
        foreach (var chromaticityGamut in this.chromaticityGamuts)
        {
            var borderPen = chromaticityGamut.BorderPen;
            if (borderPen != null)
            {
                var (rX, rY, rZ) = chromaticityGamut.ColorSpace.ToXyz(1, 0, 0);
                var (gX, gY, gZ) = chromaticityGamut.ColorSpace.ToXyz(0, 1, 0);
                var (bX, bY, bZ) = chromaticityGamut.ColorSpace.ToXyz(0, 0, 1);
                //var (wpX, wpY, wpZ) = chromaticityGamut.ColorSpace.ToXyz(1, 1, 1);
                var rXYZ = (rX + rY + rZ);
                var gXYZ = (gX + gY + gZ);
                var bXYZ = (bX + bY + bZ);
                //var wpXYZ = (wpX + wpY + wpZ);
                var rPoint = this.XYToControlCoordinate(width, height, rX / rXYZ, rY / rXYZ);
                var gPoint = this.XYToControlCoordinate(width, height, gX / gXYZ, gY / gXYZ);
                var bPoint = this.XYToControlCoordinate(width, height, bX / bXYZ, bY / bXYZ);
                //var wpPoint = this.XYToControlCoordinate(width, height, wpX / wpXYZ, wpY / wpXYZ);
                context.DrawLine(borderPen, rPoint, gPoint);
                context.DrawLine(borderPen, gPoint, bPoint);
                context.DrawLine(borderPen, bPoint, rPoint);
                //context.DrawRectangle(null, borderPen, new Rect(wpPoint.X - 2, wpPoint.Y - 2, 4, 4));
            }
        }

        // draw chromaticities
        foreach (var chromaticity in this.chromaticities)
        {
            var borderPen = chromaticity.BorderPen;
            var x = chromaticity.X;
            var y = chromaticity.Y;
            if (borderPen != null && x >= 0 && x <= MaxCoordinateX && y >= 0 && y <= MaxCoordinateY)
            {
                var point = this.XYToControlCoordinate(width, height, x, y);
                context.DrawRectangle(null, borderPen, new Rect(point.X - 2, point.Y - 2, 4, 4));
            }
        }

        // draw grid
        if (this.gridPen != null)
        {
            for (var y = MaxCoordinateY - 0.1; y > -0.05; y -= 0.1)
            {
                var lineY = y / MaxCoordinateY * height;
                context.DrawLine(this.gridPen, new Point(0, lineY), new Point(width, lineY));
            }
            for (var x = MaxCoordinateX; Math.Abs(x) > 0.05; x -= 0.1)
            {
                var lineX = x / MaxCoordinateX * width;
                context.DrawLine(this.gridPen, new Point(lineX, 0), new Point(lineX, height));
            }
        }
    }


    // Convert XY coordinate to control coordinate.
    Point XYToControlCoordinate(double width, double height, double x, double y) =>
        new Point(x / MaxCoordinateX * width, (1 - y / MaxCoordinateY) * height);


    // Interface implementations.
    Type IStyleable.StyleKey => typeof(CieChromaticityDiagram);
}


/// <summary>
/// CIE xy chromaticity shown in <see cref="CieChromaticityDiagram"/>.
/// </summary>
class CieChromaticity : AvaloniaObject
{
    /// <summary>
    /// Property of <see cref="BorderPen"/>.
    /// </summary>
    public static readonly AvaloniaProperty<IPen?> BorderPenProperty = AvaloniaProperty.Register<CieChromaticity, IPen?>(nameof(BorderPen), null);
    /// <summary>
    /// Property of <see cref="X"/>.
    /// </summary>
    public static readonly AvaloniaProperty<double> XProperty = AvaloniaProperty.Register<CieChromaticity, double>(nameof(X), 0);
    /// <summary>
    /// Property of <see cref="Y"/>.
    /// </summary>
    public static readonly AvaloniaProperty<double> YProperty = AvaloniaProperty.Register<CieChromaticity, double>(nameof(Y), 0);


    /// <summary>
    /// Get or set pen of border.
    /// </summary>
    public IPen? BorderPen
    {
        get => this.GetValue<IPen?>(BorderPenProperty);
        set => this.SetValue<IPen?>(BorderPenProperty, value);
    }


    /// <summary>
    /// Get or set x of xy chromaticity.
    /// </summary>
    public double X
    {
        get => this.GetValue<double>(XProperty);
        set => this.SetValue<double>(XProperty, value);
    }


    /// <summary>
    /// Get or set y of xy chromaticity.
    /// </summary>
    public double Y
    {
        get => this.GetValue<double>(YProperty);
        set => this.SetValue<double>(YProperty, value);
    }
}


/// <summary>
/// CIE chromaticity gamut shown in <see cref="CieChromaticityDiagram"/>.
/// </summary>
class CieChromaticityGamut : AvaloniaObject
{
    /// <summary>
    /// Property of <see cref="BorderPen"/>.
    /// </summary>
    public static readonly AvaloniaProperty<IPen?> BorderPenProperty = AvaloniaProperty.Register<CieChromaticityGamut, IPen?>(nameof(BorderPen), null);
    /// <summary>
    /// Property of <see cref="ColorSpace"/>.
    /// </summary>
    public static readonly AvaloniaProperty<Media.ColorSpace> ColorSpaceProperty = AvaloniaProperty.Register<CieChromaticityGamut, Media.ColorSpace>(nameof(ColorSpace), Media.ColorSpace.Default);


    /// <summary>
    /// Get or set pen of border.
    /// </summary>
    public IPen? BorderPen
    {
        get => this.GetValue<IPen?>(BorderPenProperty);
        set => this.SetValue<IPen?>(BorderPenProperty, value);
    }


    /// <summary>
    /// Get or set color space.
    /// </summary>
    public Media.ColorSpace ColorSpace
    {
        get => this.GetValue<Media.ColorSpace>(ColorSpaceProperty);
        set => this.SetValue<Media.ColorSpace>(ColorSpaceProperty, value);
    }
}